import { DatePipe } from '@angular/common';
import { DestroyRef, Component, OnInit, computed, effect, inject, signal, untracked, viewChild } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { verfolgeAdresse } from '../../shared/sprungziel';
import { FormsModule } from '@angular/forms';
import { AuthService } from '../../core/auth.service';
import {
  Dateiart,
  DateiartNamen,
  DocumentFolder,
  DocumentMeta,
  DocumentTarget,
  DocumentTargetKey,
  DocumentsService,
  LinkCandidate,
  SouffleurDocument,
  TargetLabels,
  dateiart,
} from '../../core/documents.service';
import { GroupSummary, GroupsService } from '../../core/groups.service';
import { DialogComponent } from '../../shared/dialog.component';
import { LesezeichenComponent } from '../../shared/lesezeichen.component';
import { DropZoneComponent } from '../../shared/drop-zone.component';
import { IconComponent } from '../../shared/icon.component';
import { MeldungComponent } from '../../shared/meldung.component';
import { ActionsRegistry, FilterGroup } from '../../shell/actions.registry';
import { VorschauComponent } from './vorschau.component';
import { VorschaubildComponent } from './vorschaubild.component';

/** Was die Ablage annimmt -- dieselbe Liste wie am Server. */
const ENDUNGEN =
  '.pdf,.jpg,.jpeg,.png,.gif,.webp,.heic,.tif,.tiff,.txt,.csv,.rtf,.xml,.zip,' +
  '.doc,.docx,.xls,.xlsx,.ppt,.pptx,.odt,.ods,.odp,.mp3,.wav,.m4a,.mp4,.mov';

const HOECHSTENS = 100 * 1024 * 1024;

/** Der Bogen im Fenster -- Schlagworte als Text, damit man sie tippen kann. */
interface Bogen {
  name: string;
  note: string;
  tags: string;
  restricted: boolean;
  groupIds: string[];

  /** Wohin: leer = wie das Modul es will, „intern" oder ein Ordner. */
  folder: string;
}

/**
 * Die Ablage des Hauses.
 *
 * Oben die Fläche zum Hineinziehen, darunter was schon da ist. Wer eine
 * Datei fallen lässt, bekommt ein Fenster für Name, Schlagworte und wer es
 * sehen darf -- und kann dort auch einfach „Ablegen" drücken. Verknüpfen
 * geht danach, am Dokument: Beim Hochladen weiß man oft noch nicht, woran
 * es hängt.
 */
@Component({
  selector: 'souffleur-dokumente',
  imports: [
    DatePipe,
    FormsModule,
    DialogComponent,
    LesezeichenComponent,
    DropZoneComponent,
    IconComponent,
    MeldungComponent,
    VorschauComponent,
    VorschaubildComponent,
  ],
  templateUrl: './dokumente.component.html',
})
export class DokumenteComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly destroyRef = inject(DestroyRef);
  private readonly documents = inject(DocumentsService);
  private readonly groupList = inject(GroupsService);
  private readonly auth = inject(AuthService);
  private readonly actions = inject(ActionsRegistry);
  private readonly ablage = viewChild(DropZoneComponent);

  protected readonly endungen = ENDUNGEN;
  protected readonly hoechstens = HOECHSTENS;
  protected readonly zielArten = Object.keys(DocumentTarget) as DocumentTargetKey[];
  protected readonly zielNamen = TargetLabels;

  protected readonly alle = signal<SouffleurDocument[]>([]);
  protected readonly schlagworte = signal<string[]>([]);
  protected readonly gruppen = signal<GroupSummary[]>([]);
  protected readonly suche = signal('');
  protected readonly schlagwort = signal('');

  /** Die Ordner auf der Synology (ADR-0013) und welcher gerade gefiltert ist. */
  protected readonly ordner = signal<DocumentFolder[]>([]);
  protected readonly ordnerFilter = signal('');

  /** Wohin ein Dokument ohne eigene Wahl geht: der Standard des Dokumentenmoduls. */
  protected readonly standardOrdner = computed(
    () => this.ordner().find((o) => o.isDefault && o.moduleKey === 'Documents') ?? null,
  );
  protected readonly problems = signal<string[]>([]);
  protected readonly hinweis = signal('');
  protected readonly busy = signal(false);
  protected readonly loading = signal(true);

  protected readonly darfAblegen = computed(() => this.auth.can('DocumentsWrite'));
  private readonly darfVerwalten = computed(() => this.auth.can('DocumentsManage'));

  /** Die gerade fallen gelassenen Dateien -- eine oder viele. */
  protected readonly dateien = signal<File[]>([]);

  /** Beim Ablegen mehrerer: wie weit es ist („2 von 5"). */
  protected readonly fortschritt = signal('');

  /**
   * Filter, die links in der Leiste stehen. Dateiart und Verknüpfung filtern
   * hier in der Liste; Schlagwort und Ordner fragen den Server, wie bisher.
   */
  protected readonly artFilter = signal<Dateiart | ''>('');
  protected readonly zielFilter = signal<DocumentTargetKey | 'ohne' | ''>('');
  protected readonly artNamen = DateiartNamen;

  /** Was die Liste nach allen Filtern zeigt -- und worin die Vorschau blättert. */
  protected readonly sichtbar = computed(() => {
    const art = this.artFilter();
    const ziel = this.zielFilter();
    return this.alle().filter(
      (d) =>
        (!art || dateiart(d) === art) &&
        (!ziel ||
          (ziel === 'ohne' ? d.links.length === 0 : d.links.some((l) => l.targetKey === ziel))),
    );
  });

  protected readonly filterAktiv = computed(
    () => !!(this.artFilter() || this.zielFilter() || this.schlagwort() || this.ordnerFilter()),
  );

  /** Welches Dokument die Vorschau zeigt -- als Stelle in `sichtbar`. */
  protected readonly vorschauStelle = signal<number | null>(null);

  /** Neu: Bogen für die gewählte Datei. Ändern: dasselbe Dokument. */
  protected readonly bogen = signal<Bogen | null>(null);
  protected readonly bearbeitet = signal<SouffleurDocument | null>(null);

  /** Verknüpfen im Ändern-Fenster. */
  protected readonly zielArt = signal<DocumentTargetKey>('Contact');
  protected readonly kandidaten = signal<LinkCandidate[]>([]);
  protected readonly gewaehlt = signal('');

  constructor() {
    // Die Filter links folgen dem, was in der Liste steht. Nur schreiben,
    // nicht lesen, was die Leiste schon hat -- sonst löste sich das selbst aus.
    effect(() => {
      const gruppen = this.filtergruppen();
      untracked(() => this.actions.filters.set(gruppen));
    });
  }

  async ngOnInit(): Promise<void> {
    // Gruppen, Zugänge und Ordner sind Einstellungen und stehen dort. Links
    // stehen hier, was man mit Dokumenten tut, und wonach man sie sucht.
    this.actions.set(
      'Dokumente',
      this.darfAblegen()
        ? [{ label: 'Dokumente ablegen', icon: 'plus', primary: true, run: () => this.ablage()?.browse() }]
        : [],
    );

    try {
      const [gruppen, ordner] = await Promise.all([
        this.groupList.active(),
        // Ohne Ordner geht alles wie bisher -- die Liste bleibt dann leer.
        this.documents.folders().catch(() => [] as DocumentFolder[]),
        this.laden(),
      ]);
      this.gruppen.set(gruppen);
      this.ordner.set(ordner);
    } finally {
      this.loading.set(false);
    }

    // Aus der Suche: `?dokument=<id>` öffnet den Bogen. Was nicht in der Liste
    // steht, darf diese Person nicht sehen -- dann passiert nichts.
    verfolgeAdresse(this.route, this.destroyRef, (params) => {
      const id = params.get('dokument');
      const doc = id ? this.alle().find((d) => d.id === id) : undefined;
      if (doc) {
        void this.bearbeiten(doc);
      }
    });

    // Wer von der Übersicht kommt, will gleich eine Datei wählen. Die
    // Fläche steht erst nach dem nächsten Zeichnen -- daher der Umweg.
    if (this.route.snapshot.queryParamMap.get('neu')) {
      setTimeout(() => this.ablage()?.browse());
    }
  }

  protected async laden(): Promise<void> {
    try {
      const [docs, tags] = await Promise.all([
        this.documents.list({
          q: this.suche() || undefined,
          tag: this.schlagwort() || undefined,
          folderId: this.ordnerFilter() || undefined,
        }),
        this.documents.tags(),
      ]);
      this.alle.set(docs);
      this.schlagworte.set(tags);
    } catch (error) {
      this.problems.set(meldungen(error));
    }
  }

  private readonly filtergruppen = computed<FilterGroup[]>(() => {
    const docs = this.alle();
    const zaehle = (pruefe: (d: SouffleurDocument) => boolean) => docs.filter(pruefe).length;
    const gruppen: FilterGroup[] = [];

    const arten = (Object.keys(DateiartNamen) as Dateiart[]).filter((a) => docs.some((d) => dateiart(d) === a));
    gruppen.push({
      key: 'art',
      heading: 'Dateityp',
      options: [
        { key: '', label: 'alle', count: docs.length, active: !this.artFilter() },
        ...arten.map((a) => ({
          key: a,
          label: DateiartNamen[a],
          count: zaehle((d) => dateiart(d) === a),
          active: this.artFilter() === a,
        })),
      ],
      choose: (key) => this.artFilter.set(key as Dateiart | ''),
    });

    const tags = this.schlagworte();
    if (tags.length) {
      gruppen.push({
        key: 'schlagwort',
        heading: 'Schlagwort',
        options: [
          { key: '', label: 'alle', active: !this.schlagwort() },
          ...tags.map((t) => ({ key: t, label: t, active: this.schlagwort() === t })),
        ],
        choose: (key) => {
          this.schlagwort.set(key);
          void this.laden();
        },
      });
    }

    const ziele = this.zielArten.filter((z) => docs.some((d) => d.links.some((l) => l.targetKey === z)));
    gruppen.push({
      key: 'ziel',
      heading: 'Verknüpft mit',
      options: [
        { key: '', label: 'egal', active: !this.zielFilter() },
        ...ziele.map((z) => ({
          key: z,
          label: this.zielNamen[z],
          count: zaehle((d) => d.links.some((l) => l.targetKey === z)),
          active: this.zielFilter() === z,
        })),
        { key: 'ohne', label: 'mit nichts', count: zaehle((d) => d.links.length === 0), active: this.zielFilter() === 'ohne' },
      ],
      choose: (key) => this.zielFilter.set(key as DocumentTargetKey | 'ohne' | ''),
    });

    const ordner = this.ordner();
    if (ordner.length) {
      gruppen.push({
        key: 'ordner',
        heading: 'Ordner',
        options: [
          { key: '', label: 'alle', active: !this.ordnerFilter() },
          ...ordner.map((o) => ({ key: o.id, label: o.name, active: this.ordnerFilter() === o.id })),
        ],
        choose: (key) => {
          this.ordnerFilter.set(key);
          void this.laden();
        },
      });
    }

    return gruppen;
  });

  protected filterZuruecksetzen(): void {
    this.artFilter.set('');
    this.zielFilter.set('');
    const neuLaden = !!(this.schlagwort() || this.ordnerFilter());
    this.schlagwort.set('');
    this.ordnerFilter.set('');
    if (neuLaden) {
      void this.laden();
    }
  }

  // ── Vorschau ───────────────────────────────────────────────────────────

  protected ansehen(doc: SouffleurDocument): void {
    const stelle = this.sichtbar().indexOf(doc);
    this.vorschauStelle.set(stelle < 0 ? 0 : stelle);
  }

  protected vorschauDarfAendern(): boolean {
    const doc = this.sichtbar()[this.vorschauStelle() ?? 0];
    return doc ? this.darfAendern(doc) : false;
  }

  protected ausVorschauAendern(doc: SouffleurDocument): void {
    this.vorschauStelle.set(null);
    void this.bearbeiten(doc);
  }

  protected darfAendern(doc: SouffleurDocument): boolean {
    return this.darfVerwalten() || (doc.isMine && this.darfAblegen());
  }

  protected fileUrl(doc: SouffleurDocument): string {
    return this.documents.fileUrl(doc.id);
  }

  protected groesse(bytes: number): string {
    if (bytes < 1024) {
      return `${bytes} B`;
    }
    if (bytes < 1024 * 1024) {
      return `${Math.round(bytes / 1024)} KB`;
    }
    return `${(bytes / 1024 / 1024).toFixed(1).replace('.', ',')} MB`;
  }

  // ── Ablegen ────────────────────────────────────────────────────────────

  /**
   * Eine Datei: Name, Schlagworte, wer es sieht -- wie bisher. Mehrere: ein
   * Bogen für alle, jede behält ihren Dateinamen. Wer zwanzig Probenfotos
   * ablegt, will nicht zwanzigmal „Ablegen" drücken.
   */
  protected dateienGewaehlt(files: File[]): void {
    if (files.length === 0) {
      return;
    }
    // Liegt schon ein Bogen zum Ablegen offen, kommen weitere Dateien dazu.
    const offen = this.bogen() !== null && this.bearbeitet() === null;
    const alle = offen ? [...this.dateien(), ...files] : files;
    this.dateien.set(alle);
    this.problems.set([]);
    this.hinweis.set('');
    this.bearbeitet.set(null);
    if (!offen) {
      this.bogen.set({
        name: ohneEndung(files[0].name),
        note: '',
        tags: '',
        restricted: false,
        groupIds: [],
        folder: '',
      });
    }
  }

  protected dateiEntfernen(file: File): void {
    const rest = this.dateien().filter((f) => f !== file);
    this.dateien.set(rest);
    if (rest.length === 0) {
      this.schliessen();
    } else if (rest.length === 1) {
      this.aendern({ name: ohneEndung(rest[0].name) });
    }
  }

  // ── Ändern ─────────────────────────────────────────────────────────────

  protected async bearbeiten(doc: SouffleurDocument): Promise<void> {
    this.problems.set([]);
    this.dateien.set([]);
    this.bearbeitet.set(doc);
    this.bogen.set({
      name: doc.name,
      note: doc.note ?? '',
      tags: doc.tags.join(', '),
      restricted: doc.isRestricted,
      groupIds: doc.groups.map((g) => g.id),
      folder: doc.folderId ?? '',
    });
    this.gewaehlt.set('');
    await this.zielArtWechseln(this.zielArt());
  }

  protected async zielArtWechseln(art: DocumentTargetKey): Promise<void> {
    this.zielArt.set(art);
    this.gewaehlt.set('');
    this.kandidaten.set(await this.documents.candidates(art));
  }

  protected aendern(change: Partial<Bogen>): void {
    this.bogen.update((b) => (b ? { ...b, ...change } : b));
  }

  protected umschalten(liste: readonly string[], id: string): string[] {
    return liste.includes(id) ? liste.filter((x) => x !== id) : [...liste, id];
  }

  protected schliessen(): void {
    this.bogen.set(null);
    this.bearbeitet.set(null);
    this.dateien.set([]);
    this.fortschritt.set('');
    this.problems.set([]);
  }

  private meta(b: Bogen): DocumentMeta {
    return {
      name: b.name.trim() || null,
      note: b.note.trim() || null,
      tags: b.tags
        .split(',')
        .map((t) => t.trim())
        .filter((t) => t.length > 0),
      restricted: b.restricted,
      groupIds: b.restricted ? b.groupIds : [],
      folder: b.folder || undefined,
    };
  }

  protected async speichern(): Promise<void> {
    const b = this.bogen();
    if (!b) {
      return;
    }
    if (b.restricted && b.groupIds.length === 0) {
      this.problems.set(['Wer soll es sehen? Eingeschränkt heißt: mindestens eine Gruppe.']);
      return;
    }

    this.busy.set(true);
    this.problems.set([]);
    try {
      const doc = this.bearbeitet();
      if (doc) {
        await this.documents.update(doc.id, this.meta(b));
      } else {
        const fehler = await this.alleAblegen(b);
        if (fehler.length) {
          // Was geklappt hat, ist abgelegt; im Bogen bleibt nur, was nicht ging.
          this.problems.set(fehler);
          await this.laden();
          return;
        }
      }
      this.schliessen();
      await this.laden();
    } catch (error) {
      this.problems.set(meldungen(error));
    } finally {
      this.busy.set(false);
      this.fortschritt.set('');
    }
  }

  /** Eine nach der anderen -- ein 100-MB-Mitschnitt soll nicht mit neunzehn Fotos um die Leitung ringen. */
  private async alleAblegen(b: Bogen): Promise<string[]> {
    const dateien = this.dateien();
    const fehler: string[] = [];
    const doppelt: string[] = [];
    const uebrig: File[] = [];

    for (const [i, file] of dateien.entries()) {
      if (dateien.length > 1) {
        this.fortschritt.set(`${i + 1} von ${dateien.length}: ${file.name}`);
      }
      const meta = { ...this.meta(b), name: dateien.length === 1 ? this.meta(b).name : ohneEndung(file.name) };
      try {
        const neu = await this.documents.upload(file, meta);
        if (neu.sameFileAs?.length) {
          doppelt.push(...neu.sameFileAs);
        }
      } catch (error) {
        fehler.push(`${file.name}: ${meldungen(error).join(' ')}`);
        uebrig.push(file);
      }
    }

    this.dateien.set(uebrig);
    if (doppelt.length) {
      this.hinweis.set(`Dieselbe Datei liegt schon als „${doppelt.join('", „')}" in der Ablage.`);
    }
    return fehler;
  }

  protected async verknuepfen(): Promise<void> {
    const doc = this.bearbeitet();
    const ziel = this.gewaehlt();
    if (!doc || !ziel) {
      return;
    }
    try {
      await this.documents.link(doc.id, DocumentTarget[this.zielArt()], ziel);
      await this.nachladen(doc.id);
      this.gewaehlt.set('');
    } catch (error) {
      this.problems.set(meldungen(error));
    }
  }

  protected async loesen(linkId: string): Promise<void> {
    const doc = this.bearbeitet();
    if (!doc) {
      return;
    }
    await this.documents.unlink(doc.id, linkId);
    await this.nachladen(doc.id);
  }

  /** Nach dem Verknüpfen: Liste neu, und das Fenster zeigt den frischen Stand. */
  private async nachladen(id: string): Promise<void> {
    await this.laden();
    const frisch = this.alle().find((d) => d.id === id);
    if (frisch) {
      this.bearbeitet.set(frisch);
    }
  }

  protected async wegwerfen(): Promise<void> {
    const doc = this.bearbeitet();
    if (!doc) {
      return;
    }
    this.busy.set(true);
    try {
      await this.documents.remove(doc.id);
      this.schliessen();
      await this.laden();
    } catch (error) {
      this.problems.set(meldungen(error));
    } finally {
      this.busy.set(false);
    }
  }
}

function meldungen(error: unknown): string[] {
  const body = (error as { error?: { errors?: Record<string, string[]>; detail?: string } })?.error;
  const felder = body?.errors ? Object.values(body.errors).flat() : [];
  return felder.length > 0 ? felder : [body?.detail ?? 'Das hat nicht geklappt.'];
}

function ohneEndung(name: string): string {
  return name.replace(/\.[^.]+$/, '');
}
