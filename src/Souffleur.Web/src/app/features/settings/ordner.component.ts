import { DatePipe } from '@angular/common';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AuthService } from '../../core/auth.service';
import { DocumentFolder, DocumentsService, FolderEntry } from '../../core/documents.service';
import { GroupSummary, GroupsService } from '../../core/groups.service';
import { DialogComponent } from '../../shared/dialog.component';
import { MeldungComponent } from '../../shared/meldung.component';
import { ActionsRegistry } from '../../shell/actions.registry';
import { messagesFrom } from '../calendar/event-dialog.component';
import { einstellungsBereiche } from './bereiche';

/** Die Module, die ablegen -- dieselben Zahlen wie am Server. */
const MODULE = [
  { value: 10, label: 'Dokumente' },
  { value: 2, label: 'Finanzen' },
  { value: 12, label: 'Produktionen' },
  { value: 5, label: 'Aufgaben' },
  { value: 11, label: 'Touring' },
  { value: 3, label: 'Personal' },
  { value: 7, label: 'Kontakte' },
  { value: 9, label: 'Kalender' },
] as const;

interface Bogen {
  name: string;
  path: string;
  module: number;
  isDefault: boolean;
  isRestricted: boolean;
  groupIds: string[];
}

/**
 * Die Ordner der Ablage auf der Synology (ADR-0013).
 *
 * Das Haus hängt seine Freigaben in den Container ein; hier wählt es darin
 * die Ordner aus, die souffleur kennen soll, und sagt, welches Modul wohin
 * ablegt. Gewählt wird durch Klicken, nicht durch Tippen eines Pfads --
 * ein Tippfehler hieße sonst ein leerer Ordner, der nie etwas findet.
 */
@Component({
  selector: 'souffleur-ordner',
  imports: [DatePipe, FormsModule, DialogComponent, MeldungComponent],
  templateUrl: './ordner.component.html',
})
export class OrdnerComponent implements OnInit {
  private readonly documents = inject(DocumentsService);
  private readonly groupList = inject(GroupsService);
  private readonly actions = inject(ActionsRegistry);
  private readonly auth = inject(AuthService);

  protected readonly module = MODULE;

  protected readonly loading = signal(true);
  protected readonly busy = signal(false);
  protected readonly problems = signal<string[]>([]);
  protected readonly hinweis = signal('');

  protected readonly status = signal<{ available: boolean; root: string } | null>(null);
  protected readonly ordner = signal<DocumentFolder[]>([]);
  protected readonly gruppen = signal<GroupSummary[]>([]);

  /** Der offene Bogen: null zu, 'neu' oder die Kennung des Ordners. */
  protected readonly bearbeitet = signal<string | null>(null);
  protected readonly bogen = signal<Bogen>(leer());

  /** Wo der Auswähler gerade steht, und was darunter liegt. */
  protected readonly ort = signal<string | null>(null);
  protected readonly eintraege = signal<FolderEntry[]>([]);
  protected readonly neuerOrdner = signal('');

  /** Der Weg zum Ort als klickbare Stufen. */
  protected readonly stufen = computed(() => {
    const teile = (this.ort() ?? '').split('/').filter((t) => t.length > 0);
    return teile.map((name, i) => ({ name, path: teile.slice(0, i + 1).join('/') }));
  });

  protected readonly finanzOrdner = computed(() =>
    this.ordner().find((o) => o.moduleKey === 'Finance' && o.isDefault),
  );

  protected readonly darfFinanzen = computed(() => this.auth.can('FinanceRead'));

  async ngOnInit(): Promise<void> {
    this.actions.set('Einstellungen', [], [
      ...einstellungsBereiche(this.auth.can('AccountsManage')),
      { label: 'Dokumente', icon: 'dokumente', path: '/dokumente' },
    ]);

    try {
      const [status, gruppen] = await Promise.all([
        this.documents.folderStatus(),
        this.groupList.active(),
        this.laden(),
      ]);
      this.status.set(status);
      this.gruppen.set(gruppen);
    } catch (error) {
      this.problems.set(messagesFrom(error));
    } finally {
      this.loading.set(false);
    }
  }

  protected async laden(): Promise<void> {
    this.ordner.set(await this.documents.folders());
  }

  // ── Bogen ────────────────────────────────────────────────────────────

  protected async neu(): Promise<void> {
    this.problems.set([]);
    this.bogen.set(leer());
    this.bearbeitet.set('neu');
    await this.gehe(null);
  }

  protected async aendern(o: DocumentFolder): Promise<void> {
    this.problems.set([]);
    this.bogen.set({
      name: o.name,
      path: o.path,
      module: o.module,
      isDefault: o.isDefault,
      isRestricted: o.isRestricted,
      groupIds: o.groups.map((g) => g.id),
    });
    this.bearbeitet.set(o.id);
    const oben = o.path.includes('/') ? o.path.slice(0, o.path.lastIndexOf('/')) : null;
    await this.gehe(oben);
  }

  protected patch(change: Partial<Bogen>): void {
    this.bogen.update((b) => ({ ...b, ...change }));
  }

  protected umschalten(id: string): void {
    this.bogen.update((b) => ({
      ...b,
      groupIds: b.groupIds.includes(id) ? b.groupIds.filter((g) => g !== id) : [...b.groupIds, id],
    }));
  }

  protected schliessen(): void {
    this.bearbeitet.set(null);
    this.problems.set([]);
  }

  /** In einen Ordner hinein -- oder zurück nach oben. */
  protected async gehe(path: string | null): Promise<void> {
    try {
      this.eintraege.set(await this.documents.browse(path));
      this.ort.set(path);
    } catch (error) {
      this.problems.set(messagesFrom(error));
    }
  }

  /** Diesen Ordner nehmen. Der Name schlägt sich aus dem letzten Teil vor. */
  protected waehle(entry: FolderEntry): void {
    const b = this.bogen();
    this.patch({ path: entry.path, name: b.name || entry.name });
  }

  protected async anlegen(): Promise<void> {
    const name = this.neuerOrdner().trim();
    if (!name) {
      return;
    }
    try {
      const entry = await this.documents.createDirectory(this.ort(), name);
      this.neuerOrdner.set('');
      await this.gehe(this.ort());
      this.waehle(entry);
    } catch (error) {
      this.problems.set(messagesFrom(error));
    }
  }

  protected async speichern(): Promise<void> {
    const id = this.bearbeitet();
    const b = this.bogen();
    this.busy.set(true);
    this.problems.set([]);
    try {
      const gespeichert = await this.documents.saveFolder(id === 'neu' ? null : id, {
        ...b,
        groupIds: b.isRestricted ? b.groupIds : [],
      });
      this.hinweis.set(`„${gespeichert.name}“ eingelesen: ${gespeichert.lastScanResult ?? 'nichts Neues'}.`);
      this.bearbeitet.set(null);
      await this.laden();
    } catch (error) {
      this.problems.set(messagesFrom(error));
    } finally {
      this.busy.set(false);
    }
  }

  protected async entfernen(): Promise<void> {
    const id = this.bearbeitet();
    if (!id || id === 'neu') {
      return;
    }
    await this.documents.removeFolder(id);
    this.bearbeitet.set(null);
    await this.laden();
  }

  // ── Einlesen ─────────────────────────────────────────────────────────

  protected async einlesen(o: DocumentFolder): Promise<void> {
    this.busy.set(true);
    try {
      const ergebnis = await this.documents.scanFolder(o.id);
      this.hinweis.set(`„${o.name}“: ${ergebnis}.`);
      await this.laden();
    } catch (error) {
      this.problems.set(messagesFrom(error));
    } finally {
      this.busy.set(false);
    }
  }

  protected async belegeKopieren(): Promise<void> {
    this.busy.set(true);
    try {
      const anzahl = await this.documents.copyVouchers();
      this.hinweis.set(
        anzahl === 0
          ? 'Alle Belege haben schon eine Kopie im Finanzordner.'
          : `${anzahl} Belege in den Finanzordner kopiert.`,
      );
      await this.laden();
    } catch (error) {
      this.problems.set(messagesFrom(error));
    } finally {
      this.busy.set(false);
    }
  }
}

function leer(): Bogen {
  return { name: '', path: '', module: 10, isDefault: true, isRestricted: false, groupIds: [] };
}
