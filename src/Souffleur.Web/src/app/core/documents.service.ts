import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

/** Woran ein Dokument hängen kann -- dieselben Zahlen wie am Server. */
export const DocumentTarget = {
  Contact: 1,
  Task: 2,
  Event: 3,
  JournalEntry: 4,
  Voucher: 5,
  TourStop: 6,
  Production: 7,
} as const;

export type DocumentTargetKey = keyof typeof DocumentTarget;

export const TargetLabels: Record<DocumentTargetKey, string> = {
  Contact: 'Kontakt',
  Task: 'Aufgabe',
  Event: 'Termin',
  JournalEntry: 'Buchung',
  Voucher: 'Beleg',
  TourStop: 'Gastspiel',
  Production: 'Produktion',
};

export interface DocumentLink {
  id: string;
  target: number;
  targetKey: DocumentTargetKey;
  targetId: string;
  label: string;
}

export interface SouffleurDocument {
  id: string;
  name: string;
  originalName: string;
  contentType: string;
  sizeInBytes: number;
  note: string | null;
  isRestricted: boolean;
  tags: string[];
  groups: { id: string; name: string }[];
  links: DocumentLink[];
  createdAt: string;

  /** Selbst abgelegt -- dann darf man es auch ändern. */
  isMine: boolean;

  /** Der Ordner auf der Synology, wenn die Datei dort liegt (ADR-0013). */
  folderId: string | null;
  folderName: string | null;

  /** Der Pfad, wie ihn die File Station zeigt. */
  folderPath: string | null;

  /** Die Datei fehlt im Ordner -- vielleicht verschoben, vielleicht gelöscht. */
  isMissing: boolean;

  /** Nur nach dem Hochladen: gleiche Datei, die schon in der Ablage liegt. */
  sameFileAs?: string[] | null;
}

export interface DocumentMeta {
  name: string | null;
  note: string | null;
  tags: string[];
  restricted: boolean;
  groupIds: string[];

  /**
   * Wohin beim Hochladen: leer lässt das Modul entscheiden, „intern" hält die
   * Datei bei souffleur, sonst die Kennung eines Ordners.
   */
  folder?: string;
}

/** Ein Ordner der Ablage auf der Synology. */
export interface DocumentFolder {
  id: string;
  name: string;
  path: string;
  module: number;
  moduleKey: string;
  moduleLabel: string;
  isDefault: boolean;
  isRestricted: boolean;
  groups: { id: string; name: string }[];
  lastScanAt: string | null;
  lastScanResult: string | null;
  files: number;
  missing: number;
}

export interface FolderInput {
  name: string;
  path: string;
  module: number;
  isDefault: boolean;
  isRestricted: boolean;
  groupIds: string[];
}

/** Ein Verzeichnis unterhalb der Ablage, zum Durchklicken. */
export interface FolderEntry {
  name: string;
  path: string;
  hasChildren: boolean;
}

/** Eine Wahlmöglichkeit beim Verknüpfen. */
export interface LinkCandidate {
  id: string;
  label: string;
}

/**
 * Die Ablage des Hauses. Wer was sieht, entscheidet der Server; die
 * Oberfläche zeigt, was zurückkommt.
 */
@Injectable({ providedIn: 'root' })
export class DocumentsService {
  private readonly http = inject(HttpClient);

  list(
    filter: { q?: string; tag?: string; target?: number; targetId?: string; folderId?: string } = {},
  ): Promise<SouffleurDocument[]> {
    let params = new HttpParams();
    if (filter.folderId) {
      params = params.set('folderId', filter.folderId);
    }
    if (filter.q) {
      params = params.set('q', filter.q);
    }
    if (filter.tag) {
      params = params.set('tag', filter.tag);
    }
    if (filter.target && filter.targetId) {
      params = params.set('target', filter.target).set('targetId', filter.targetId);
    }
    return firstValueFrom(this.http.get<SouffleurDocument[]>('/api/documents', { params }));
  }

  tags(): Promise<string[]> {
    return firstValueFrom(this.http.get<string[]>('/api/documents/tags'));
  }

  /** Die Adresse zum Herunterladen -- als Link, damit der Browser die Datei selbst holt. */
  fileUrl(id: string): string {
    return `/api/documents/${id}/file`;
  }

  /** Dieselbe Datei zum Anzeigen statt Herunterladen -- der Server entscheidet, ob er darf. */
  previewUrl(id: string): string {
    return `/api/documents/${id}/file?inline=true`;
  }

  upload(file: File, meta: DocumentMeta): Promise<SouffleurDocument> {
    const form = new FormData();
    form.append('file', file, file.name);
    if (meta.name) {
      form.append('name', meta.name);
    }
    if (meta.note) {
      form.append('note', meta.note);
    }
    for (const tag of meta.tags) {
      form.append('tags', tag);
    }
    form.append('restricted', String(meta.restricted));
    for (const id of meta.groupIds) {
      form.append('groupIds', id);
    }
    if (meta.folder) {
      form.append('folder', meta.folder);
    }
    return firstValueFrom(this.http.post<SouffleurDocument>('/api/documents', form));
  }

  // ── Ordner auf der Synology ────────────────────────────────────────────

  folders(): Promise<DocumentFolder[]> {
    return firstValueFrom(this.http.get<DocumentFolder[]>('/api/documents/folders'));
  }

  folderStatus(): Promise<{ available: boolean; root: string }> {
    return firstValueFrom(
      this.http.get<{ available: boolean; root: string }>('/api/documents/folders/status'),
    );
  }

  browse(path: string | null): Promise<FolderEntry[]> {
    const params = path ? new HttpParams().set('path', path) : undefined;
    return firstValueFrom(
      this.http.get<FolderEntry[]>('/api/documents/folders/browse', { params }),
    );
  }

  createDirectory(parent: string | null, name: string): Promise<FolderEntry> {
    return firstValueFrom(
      this.http.post<FolderEntry>('/api/documents/folders/directories', { parent, name }),
    );
  }

  saveFolder(id: string | null, input: FolderInput): Promise<DocumentFolder> {
    return firstValueFrom(
      id
        ? this.http.put<DocumentFolder>(`/api/documents/folders/${id}`, input)
        : this.http.post<DocumentFolder>('/api/documents/folders', input),
    );
  }

  async removeFolder(id: string): Promise<void> {
    await firstValueFrom(this.http.delete(`/api/documents/folders/${id}`));
  }

  async scanFolder(id: string): Promise<string> {
    return (
      await firstValueFrom(this.http.post<{ result: string }>(`/api/documents/folders/${id}/scan`, {}))
    ).result;
  }

  async copyVouchers(): Promise<number> {
    return (
      await firstValueFrom(
        this.http.post<{ copied: number }>('/api/documents/folders/copy-vouchers', {}),
      )
    ).copied;
  }

  async update(id: string, meta: DocumentMeta): Promise<void> {
    await firstValueFrom(this.http.put(`/api/documents/${id}`, meta));
  }

  async remove(id: string): Promise<void> {
    await firstValueFrom(this.http.delete(`/api/documents/${id}`));
  }

  async link(id: string, target: number, targetId: string): Promise<void> {
    await firstValueFrom(this.http.post(`/api/documents/${id}/links`, { target, targetId }));
  }

  async unlink(id: string, linkId: string): Promise<void> {
    await firstValueFrom(this.http.delete(`/api/documents/${id}/links/${linkId}`));
  }

  /**
   * Was sich anhängen lässt. Je Art eine eigene Quelle -- und still leer,
   * wenn das Modul dahinter abgeschaltet ist oder das Recht fehlt.
   */
  async candidates(target: DocumentTargetKey): Promise<LinkCandidate[]> {
    try {
      switch (target) {
        case 'Contact': {
          const rows = await firstValueFrom(
            this.http.get<{ id: string; name: string }[]>('/api/contacts?take=500'),
          );
          return rows.map((r) => ({ id: r.id, label: r.name }));
        }
        case 'Task': {
          const rows = await firstValueFrom(
            this.http.get<{ id: string; title: string }[]>('/api/tasks?includeClosed=true'),
          );
          return rows.map((r) => ({ id: r.id, label: r.title }));
        }
        case 'Event': {
          // Ein halbes Jahr in beide Richtungen. Serienvorkommen tragen
          // keinen eigenen Termin und fallen deshalb heraus.
          const heute = new Date();
          const von = new Date(heute.getFullYear(), heute.getMonth() - 6, 1);
          const bis = new Date(heute.getFullYear(), heute.getMonth() + 6, 28);
          const iso = (d: Date) => d.toISOString().slice(0, 10);
          const result = await firstValueFrom(
            this.http.get<{ occurrences: { eventId: string | null; title: string; startsAt: string }[] }>(
              `/api/calendar/range?from=${iso(von)}&to=${iso(bis)}`,
            ),
          );
          return result.occurrences
            .filter((o) => o.eventId)
            .map((o) => ({
              id: o.eventId!,
              label: `${o.title} · ${new Date(o.startsAt).toLocaleDateString('de-DE')}`,
            }));
        }
        case 'Voucher': {
          const rows = await firstValueFrom(
            this.http.get<{ id: string; label: string; description: string }[]>('/api/accounting/vouchers?take=500'),
          );
          return rows.map((r) => ({ id: r.id, label: `${r.label} · ${r.description}` }));
        }
        case 'JournalEntry': {
          const rows = await firstValueFrom(
            this.http.get<{ id: string; description: string; date: string }[]>('/api/accounting/journal?take=200'),
          );
          return rows.map((r) => ({ id: r.id, label: `${r.date} · ${r.description}` }));
        }
        case 'Production': {
          // Abgespieltes auch -- der Förderbescheid kommt oft erst danach.
          const rows = await firstValueFrom(
            this.http.get<{ id: string; title: string; subtitle: string | null; statusLabel: string }[]>(
              '/api/productions?includeArchived=true',
            ),
          );
          return rows.map((r) => ({
            id: r.id,
            label: r.subtitle ? `${r.title} · ${r.subtitle}` : r.title,
          }));
        }
        case 'TourStop': {
          // Ist Touring aus, antwortet der Server mit 404 -- dann bleibt die
          // Liste leer, und das Ziel steht zwar in der Auswahl, hat aber nichts.
          const tours = await firstValueFrom(
            this.http.get<{ stops: { id: string; title: string; arrivalDate: string | null }[] }[]>(
              '/api/touring/tours',
            ),
          );
          return tours
            .flatMap((t) => t.stops)
            .map((s) => ({
              id: s.id,
              label: s.arrivalDate
                ? `${s.title} · ${new Date(`${s.arrivalDate}T12:00:00Z`).toLocaleDateString('de-DE')}`
                : s.title,
            }));
        }
      }
    } catch {
      return [];
    }
  }
}

/** Wie eine Datei sich zeigen lässt -- danach richten sich Vorschaubild, Vorschau und Filter. */
export type Dateiart = 'pdf' | 'bild' | 'audio' | 'video' | 'text' | 'office' | 'sonstige';

export const DateiartNamen: Record<Dateiart, string> = {
  pdf: 'PDF',
  bild: 'Bilder',
  office: 'Office',
  audio: 'Audio',
  video: 'Video',
  text: 'Text',
  sonstige: 'Sonstige',
};

const ARTEN: Record<string, Dateiart> = {
  pdf: 'pdf',
  png: 'bild',
  jpg: 'bild',
  jpeg: 'bild',
  gif: 'bild',
  webp: 'bild',
  heic: 'bild',
  tif: 'bild',
  tiff: 'bild',
  mp3: 'audio',
  wav: 'audio',
  m4a: 'audio',
  mp4: 'video',
  mov: 'video',
  txt: 'text',
  csv: 'text',
  rtf: 'office',
  doc: 'office',
  docx: 'office',
  xls: 'office',
  xlsx: 'office',
  ppt: 'office',
  pptx: 'office',
  odt: 'office',
  ods: 'office',
  odp: 'office',
};

/** Nach der Endung -- der Typ ist bei Dateien aus der File Station oft nur „octet-stream". */
export function dateiart(doc: Pick<SouffleurDocument, 'originalName'>): Dateiart {
  return ARTEN[endung(doc)] ?? 'sonstige';
}

export function endung(doc: Pick<SouffleurDocument, 'originalName'>): string {
  const punkt = doc.originalName.lastIndexOf('.');
  return punkt < 0 ? '' : doc.originalName.slice(punkt + 1).toLowerCase();
}

/**
 * Was der Browser selbst zeigen kann. TIFF kann er nicht, HEIC nur Safari --
 * beides bekommt trotzdem ein Vorschaubild, wenn es lädt, sonst das Zeichen.
 */
export function zeigbar(doc: Pick<SouffleurDocument, 'originalName'>): boolean {
  const art = dateiart(doc);
  const e = endung(doc);
  return art === 'pdf' || art === 'audio' || art === 'video' || art === 'text' || (art === 'bild' && e !== 'tif' && e !== 'tiff');
}
