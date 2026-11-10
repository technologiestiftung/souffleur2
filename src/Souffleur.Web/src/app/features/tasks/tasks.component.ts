import { NgTemplateOutlet } from '@angular/common';
import {
  DestroyRef,
  Component,
  ElementRef,
  OnInit,
  computed,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { verfolgeAdresse } from '../../shared/sprungziel';
import { CatalogEntry, CatalogsService } from '../../core/catalogs.service';
import { IconComponent } from '../../shared/icon.component';
import { Person, PeopleService } from '../../core/people.service';
import { GroupSummary, GroupsService } from '../../core/groups.service';
import { Production, ProductionsService } from '../../core/productions.service';
import { TaskItem, TaskState, TaskStateInfo, TasksService } from '../../core/tasks.service';
import { CostCenter } from '../../core/api';
import { JournalService } from '../../core/journal.service';
import { DocumentTarget, DocumentsService, SouffleurDocument } from '../../core/documents.service';
import { BookmarkDraft, BookmarksService } from '../../core/bookmarks.service';
import { ModulesService } from '../../core/modules.service';
import { DialogComponent } from '../../shared/dialog.component';
import { LesezeichenComponent } from '../../shared/lesezeichen.component';
import { MehrfachauswahlComponent } from '../../shared/mehrfachauswahl.component';
import { ActionsRegistry } from '../../shell/actions.registry';
import { MetricStripComponent } from '../../shared/metric-strip.component';
import { TaskTimelineComponent } from './timeline.component';
import { messagesFrom } from '../calendar/event-dialog.component';
import { MeldungComponent } from '../../shared/meldung.component';

/** Was im Bogen steht: die Aufgabe, dazu die Listen, die nicht in `TaskItem` liegen. */
type Draft = Partial<TaskItem> & {
  participantIds?: string[];
  contactIds?: string[];
};

/**
 * Die Aufgaben des Hauses -- siehe ADR-0007.
 *
 * Sortiert nach Frist, nicht nach Anlagedatum: Was als Nächstes dran ist,
 * steht oben. Teilaufgaben hängen unter ihrer Aufgabe, damit eine Checkliste
 * als eine Sache erscheint und nicht als fünf lose Zeilen.
 */
@Component({
  selector: 'souffleur-tasks',
  imports: [
    FormsModule,
    NgTemplateOutlet,
    TaskTimelineComponent,
    MetricStripComponent,
    DialogComponent,
    MeldungComponent,
    IconComponent,
    LesezeichenComponent,
    MehrfachauswahlComponent,
  ],
  templateUrl: './tasks.component.html',
  host: { class: 'block h-full min-h-0' },
})
export class TasksComponent implements OnInit {
  private readonly tasks = inject(TasksService);
  private readonly route = inject(ActivatedRoute);
  private readonly destroyRef = inject(DestroyRef);
  private readonly catalogs = inject(CatalogsService);
  private readonly peopleList = inject(PeopleService);
  private readonly groupList = inject(GroupsService);
  private readonly productionList = inject(ProductionsService);
  private readonly journal = inject(JournalService);
  private readonly documents = inject(DocumentsService);
  private readonly bookmarks = inject(BookmarksService);
  private readonly modules = inject(ModulesService);
  private readonly actions = inject(ActionsRegistry);

  protected readonly loading = this.tasks.loading;

  protected readonly categories = signal<CatalogEntry[]>([]);
  protected readonly people = signal<Person[]>([]);

  /** Alle Kontakte, auch Häuser und Agenturen -- für die Verknüpfungen. */
  protected readonly contacts = signal<Person[]>([]);
  protected readonly groups = signal<GroupSummary[]>([]);
  protected readonly productions = signal<Production[]>([]);
  protected readonly costCenters = signal<CostCenter[]>([]);
  protected readonly states = signal<TaskStateInfo[]>([]);

  protected readonly category = signal<string>('');
  protected readonly assignee = signal<string>('');
  protected readonly includeClosed = signal(false);

  /** Nur was an meinen Gruppen hängt -- „was liegt bei der Technik?". */
  protected readonly meineGruppen = signal(false);

  protected readonly problems = signal<string[]>([]);
  protected readonly busy = signal(false);

  protected readonly editing = signal<string | null>(null);
  protected readonly draft = signal<Draft>({});

  /** Unter welcher Aufgabe gerade eine Teilaufgabe entsteht. */
  protected readonly addingTo = signal<string | null>(null);

  // ── Dokumente am Bogen ──────────────────────────────────────────────
  /** Was an der Aufgabe hängt -- aus der Ablage, die weiß, wer was sehen darf. */
  protected readonly linkedDocuments = signal<SouffleurDocument[]>([]);

  /** Was sich anheften ließe. */
  protected readonly documentChoices = signal<SouffleurDocument[]>([]);
  protected readonly chosenDocument = signal('');

  /**
   * Beim Anlegen gibt es die Aufgabe noch nicht, also auch nichts, woran ein
   * Dokument hängen könnte. Gesammelt wird trotzdem -- und nach dem
   * Speichern angeheftet, in derselben Reihenfolge.
   */
  protected readonly pendingDocumentIds = signal<string[]>([]);
  protected readonly pendingFiles = signal<File[]>([]);
  protected readonly pendingBookmarks = signal<BookmarkDraft[]>([]);

  protected readonly fileField = viewChild<ElementRef<HTMLInputElement>>('datei');

  protected readonly taskStates = TaskState;

  protected get documentsOn(): boolean {
    return this.modules.isEnabled('Documents');
  }

  /**
   * Liste oder Zeitstrahl. Zwei Fragen an dieselben Daten: Was ist offen --
   * und wie lange dauert es, was hängt daran?
   */
  protected readonly view = signal<'liste' | 'zeitstrahl'>('liste');

  /**
   * Aufgaben mit ihren Teilaufgaben darunter. Der Server liefert eine flache
   * Liste -- die Schachtelung entsteht hier, weil nur die Anzeige sie braucht.
   */
  protected readonly grouped = computed(() => {
    const all = this.tasks.tasks();
    const children = new Map<string, TaskItem[]>();

    for (const task of all.filter((t) => t.parentId)) {
      const list = children.get(task.parentId!);
      if (list) {
        list.push(task);
      } else {
        children.set(task.parentId!, [task]);
      }
    }

    // Eine Teilaufgabe, deren Aufgabe gerade weggefiltert ist, steht für
    // sich -- sonst verschwände sie ganz.
    const ids = new Set(all.map((t) => t.id));

    return all
      .filter((t) => !t.parentId || !ids.has(t.parentId))
      .map((task) => ({ task, children: children.get(task.id) ?? [] }));
  });

  protected readonly heading = computed(() => {
    if (this.editing() !== 'neu') {
      return 'Aufgabe ändern';
    }

    return this.addingTo() ? 'Teilaufgabe anlegen' : 'Aufgabe anlegen';
  });

  /** Beim Anlegen: null, der Lesezeichen-Baustein sammelt dann; sonst die Id. */
  protected readonly editingId = computed(() => (this.editing() === 'neu' ? null : this.editing()));

  /**
   * Eine Aufgabe an einer abgespielten Produktion: Die steht nicht mehr zur
   * Wahl, soll aber im Bogen lesbar bleiben und nicht stillschweigend
   * verschwinden.
   */
  protected readonly abgespielteProduktion = computed(() => {
    const id = this.draft().productionId;
    return !!id && !this.productions().some((p) => p.id === id);
  });

  /** Die Dokumente, die noch nicht dranhängen -- für das Auswahlfeld. */
  protected readonly offeneDokumente = computed(() => {
    const drin = new Set([
      ...this.linkedDocuments().map((d) => d.id),
      ...this.pendingDocumentIds(),
    ]);
    return this.documentChoices().filter((d) => !drin.has(d.id));
  });

  /** Beim Anlegen vorgemerkte Dokumente, mit Namen zum Anzeigen. */
  protected readonly vorgemerkteDokumente = computed(() => {
    const byId = new Map(this.documentChoices().map((d) => [d.id, d]));
    return this.pendingDocumentIds()
      .map((id) => byId.get(id))
      .filter((d): d is SouffleurDocument => !!d);
  });

  protected readonly summary = computed(() => {
    const all = this.tasks.tasks();
    return {
      open: all.filter((t) => t.isOpen).length,
      overdue: all.filter((t) => t.isOverdue).length,
    };
  });

  async ngOnInit(): Promise<void> {
    this.actions.set(
      'Aufgaben',
      [
        {
          label: 'Aufgabe anlegen',
          icon: 'aufgaben',
          primary: true,
          run: () => this.startNew(null),
        },
      ],
      [
        {
          label: 'Aufgabenkategorien',
          icon: 'etikett',
          path: '/einstellungen/listen/TaskCategory',
        },
      ],
    );

    this.categories.set(await this.catalogs.entries('TaskCategory'));
    this.people.set(await this.peopleList.load());
    this.contacts.set(await this.peopleList.all());
    this.groups.set(await this.groupList.active());
    this.productions.set(await this.productionList.choices());
    this.states.set(await this.tasks.states());
    // Kostenstellen gibt es nur mit Finanzmodul und Leserecht -- sonst
    // bleibt das Feld weg, statt leer dazustehen.
    this.costCenters.set(await this.journal.costCenters().catch(() => []));
    await this.reload();

    // Wer von der Übersicht kommt, will den Bogen schon offen sehen -- aus
    // einer Produktion heraus gleich mit ihr im Feld.
    if (this.route.snapshot.queryParamMap.get('neu')) {
      this.startNew(null);
      const produktion = this.route.snapshot.queryParamMap.get('produktion');
      if (produktion) {
        this.patch({ productionId: produktion });
      }
    }

    // Aus der Suche: `?aufgabe=<id>` öffnet den Bogen; eine erledigte bringt
    // `erledigte=1` mit, weil sie sonst nicht in der Liste stünde.
    verfolgeAdresse(this.route, this.destroyRef, async (params) => {
      const id = params.get('aufgabe');
      if (!id) {
        return;
      }
      if (params.get('erledigte') && !this.includeClosed()) {
        this.includeClosed.set(true);
        await this.reload();
      }
      const task = this.tasks.tasks().find((t) => t.id === id);
      if (task) {
        this.startEdit(task);
      }
    });
  }

  protected async reload(): Promise<void> {
    await this.tasks.load({
      category: this.category() || null,
      assignee: this.assignee() || null,
      includeClosed: this.includeClosed(),
      meineGruppen: this.meineGruppen(),
    });
  }

  protected startNew(parentId: string | null): void {
    this.problems.set([]);
    this.addingTo.set(parentId);
    this.editing.set('neu');
    this.draft.set({
      title: '',
      state: TaskState.Open,
      parentId,
      participantIds: [],
      contactIds: [],
    });
    this.resetAttachments();
    void this.loadDocumentChoices();
  }

  protected startEdit(task: TaskItem): void {
    this.problems.set([]);
    this.addingTo.set(null);
    this.editing.set(task.id);
    this.draft.set({
      ...task,
      participantIds: task.participants.map((p) => p.id),
      contactIds: task.contacts.map((p) => p.id),
    });
    this.resetAttachments();
    void this.loadDocumentChoices();
    void this.loadLinkedDocuments(task.id);
  }

  protected cancel(): void {
    this.editing.set(null);
    this.addingTo.set(null);
    this.draft.set({});
    this.resetAttachments();
  }

  protected patch(change: Draft): void {
    this.draft.update((current) => ({ ...current, ...change }));
  }

  protected async save(): Promise<void> {
    const draft = this.draft();
    if (!draft.title?.trim()) {
      this.problems.set(['Ein Titel ist nötig.']);
      return;
    }

    this.busy.set(true);
    this.problems.set([]);

    const input = {
      title: draft.title.trim(),
      description: draft.description?.trim() || null,
      categoryId: draft.categoryId || null,
      assigneeId: draft.assigneeId || null,
      groupId: draft.groupId || null,
      productionId: draft.productionId || null,
      costCenterId: draft.costCenterId || null,
      participantIds: draft.participantIds ?? [],
      contactIds: draft.contactIds ?? [],
      dueDate: draft.dueDate || null,
      startDate: draft.startDate || null,
      parentId: draft.parentId || null,
      state: draft.state ?? TaskState.Open,
    };

    try {
      const saved =
        this.editing() === 'neu'
          ? await this.tasks.create(input)
          : await this.tasks.update(this.editing()!, input);

      await this.attachPending(saved.id);
      this.cancel();
      await this.reload();
    } catch (error) {
      this.problems.set(messagesFrom(error));
    } finally {
      this.busy.set(false);
    }
  }

  // ── Dokumente und Lesezeichen am Bogen ──────────────────────────────

  private resetAttachments(): void {
    this.linkedDocuments.set([]);
    this.chosenDocument.set('');
    this.pendingDocumentIds.set([]);
    this.pendingFiles.set([]);
    this.pendingBookmarks.set([]);
  }

  private async loadDocumentChoices(): Promise<void> {
    if (!this.documentsOn) {
      return;
    }
    this.documentChoices.set(await this.documents.list().catch(() => []));
  }

  private async loadLinkedDocuments(taskId: string): Promise<void> {
    if (!this.documentsOn) {
      return;
    }
    const found = await this.documents
      .list({ target: DocumentTarget.Task, targetId: taskId })
      .catch(() => []);
    // Zwischenzeitlich einen anderen Bogen geöffnet? Dann gehört das nicht hierher.
    if (this.editing() === taskId) {
      this.linkedDocuments.set(found);
    }
  }

  /** Ein vorhandenes Dokument anheften -- sofort, oder vorgemerkt bis zum Speichern. */
  protected async attachDocument(): Promise<void> {
    const id = this.chosenDocument();
    if (!id) {
      return;
    }
    this.chosenDocument.set('');

    const taskId = this.editingId();
    if (!taskId) {
      this.pendingDocumentIds.update((liste) => [...liste, id]);
      return;
    }

    try {
      await this.documents.link(id, DocumentTarget.Task, taskId);
      await this.loadLinkedDocuments(taskId);
    } catch (error) {
      this.problems.set(messagesFrom(error));
    }
  }

  protected async detachDocument(doc: SouffleurDocument): Promise<void> {
    const taskId = this.editingId();
    const link = doc.links.find((l) => l.targetKey === 'Task' && l.targetId === taskId);
    if (!taskId || !link) {
      return;
    }
    try {
      await this.documents.unlink(doc.id, link.id);
      await this.loadLinkedDocuments(taskId);
    } catch (error) {
      this.problems.set(messagesFrom(error));
    }
  }

  protected unpendDocument(id: string): void {
    this.pendingDocumentIds.update((liste) => liste.filter((x) => x !== id));
  }

  protected plusFile(): void {
    this.fileField()?.nativeElement.click();
  }

  /** Eine Datei gewählt: in die Ablage und an die Aufgabe -- oder vorgemerkt. */
  protected async fileChosen(event: Event): Promise<void> {
    const input = event.target as HTMLInputElement;
    const files = Array.from(input.files ?? []);
    input.value = '';
    if (files.length === 0) {
      return;
    }

    const taskId = this.editingId();
    if (!taskId) {
      this.pendingFiles.update((liste) => [...liste, ...files]);
      return;
    }

    this.busy.set(true);
    try {
      await this.uploadAndLink(files, taskId);
      await this.loadLinkedDocuments(taskId);
    } catch (error) {
      this.problems.set(messagesFrom(error));
    } finally {
      this.busy.set(false);
    }
  }

  protected unpendFile(index: number): void {
    this.pendingFiles.update((liste) => liste.filter((_, i) => i !== index));
  }

  private async uploadAndLink(files: File[], taskId: string): Promise<void> {
    for (const file of files) {
      const stored = await this.documents.upload(file, {
        name: null,
        note: null,
        tags: [],
        restricted: false,
        groupIds: [],
      });
      await this.documents.link(stored.id, DocumentTarget.Task, taskId);
    }
  }

  /** Nach dem Speichern: was beim Anlegen vorgemerkt wurde, jetzt wirklich anheften. */
  private async attachPending(taskId: string): Promise<void> {
    for (const id of this.pendingDocumentIds()) {
      await this.documents.link(id, DocumentTarget.Task, taskId);
    }
    await this.uploadAndLink(this.pendingFiles(), taskId);
    for (const draft of this.pendingBookmarks()) {
      await this.bookmarks.add('Task', taskId, draft);
    }
  }

  protected fileUrl(doc: SouffleurDocument): string {
    return this.documents.fileUrl(doc.id);
  }

  // ── Liste ───────────────────────────────────────────────────────────

  /** Erledigen und wieder öffnen -- die häufigste Handlung in der Liste. */
  protected async toggleDone(task: TaskItem): Promise<void> {
    await this.tasks.setState(task.id, task.isOpen ? TaskState.Done : TaskState.Open);
    await this.reload();
  }

  protected async setState(task: TaskItem, state: number): Promise<void> {
    await this.tasks.setState(task.id, Number(state));
    await this.reload();
  }

  protected async remove(task: TaskItem): Promise<void> {
    this.problems.set([]);

    try {
      await this.tasks.remove(task.id);
      await this.reload();
    } catch (error) {
      this.problems.set(messagesFrom(error));
    }
  }

  protected due(task: TaskItem): string {
    if (!task.dueDate) {
      return '';
    }

    return new Date(`${task.dueDate}T12:00:00Z`).toLocaleDateString('de-DE', {
      day: 'numeric',
      month: 'short',
    });
  }

  /** „Anna Krause, Ben Schröder" -- für den Tooltip in der Zeile. */
  protected beteiligte(task: TaskItem): string {
    return task.participants.map((p) => p.name).join(', ');
  }
}
