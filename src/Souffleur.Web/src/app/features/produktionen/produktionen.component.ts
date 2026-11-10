import { CurrencyPipe, DatePipe } from '@angular/common';
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
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { AuthService } from '../../core/auth.service';
import { CatalogEntry, CatalogsService } from '../../core/catalogs.service';
import { DocumentTarget, DocumentsService } from '../../core/documents.service';
import { ModulesService } from '../../core/modules.service';
import { Person, PeopleService } from '../../core/people.service';
import { Resource, ResourcesService } from '../../core/resources.service';
import { VoucherDirection, VoucherSource, VouchersService } from '../../core/vouchers.service';
import {
  Production,
  ProductionBudgetLineInput,
  ProductionNeedInput,
  ProductionNeedKind,
  ProductionOverview,
  ProductionRoleInput,
  ProductionStatus,
  ProductionStatusInfo,
  ProductionsService,
} from '../../core/productions.service';
import { TasksService } from '../../core/tasks.service';
import { AktivitaetComponent } from '../../shared/aktivitaet.component';
import { LesezeichenComponent } from '../../shared/lesezeichen.component';
import { DialogComponent } from '../../shared/dialog.component';
import { IconComponent } from '../../shared/icon.component';
import { MeldungComponent } from '../../shared/meldung.component';
import { MetricStripComponent } from '../../shared/metric-strip.component';
import { verfolgeAdresse } from '../../shared/sprungziel';
import { ActionsRegistry } from '../../shell/actions.registry';
import { EventDialogComponent, messagesFrom } from '../calendar/event-dialog.component';

/** Die Stammdaten einer Produktion, so wie sie im Bogen stehen. */
interface ProductionDraft {
  title: string;
  subtitle: string;
  status: number;
  premiereDate: string;
  closingDate: string;
  budget: string;
  description: string;
  research: string;
  notes: string;
}

/** Eine Budgetposition im Bogen -- der Betrag als Text, bis er gespeichert wird. */
interface BudgetLineDraft {
  title: string;
  category: string;
  amount: string;
  note: string;
}

/** Welcher Abschnitt gerade im Fenster steht. */
type Section =
  | 'neu'
  | 'stammdaten'
  | 'besetzung'
  | 'budget'
  | 'bedarf'
  | 'papiere'
  | 'termin'
  | 'beleg'
  | 'loeschen';

/** Ein Beleg, wie er aus dem Buch heraus entsteht -- das Nötigste, der Rest im Belegbogen. */
interface VoucherDraft {
  date: string;
  direction: number;
  grossAmount: string;
  description: string;
  file: File | null;
}

/** Vorschläge für die Bereiche des Budgets -- frei bleibt es trotzdem. */
export const BUDGET_CATEGORIES = [
  'Gagen',
  'Honorare',
  'Ausstattung',
  'Technik',
  'Werbung',
  'Rechte',
  'Reise',
  'Sonstiges',
] as const;

/**
 * Die Produktionen des Hauses -- siehe ADR-0011.
 *
 * Ein Produktionsbuch zum Durchblättern: links das Register, rechts eine
 * Produktion mit allem auf einen Blick -- Beschreibung, Besetzung, Budget und
 * Positionen, Bedarf, Papiere, Lesezeichen, Recherche; dazu, was die anderen Module
 * an sie geheftet haben: Aufgaben, Termine, Belege, Dokumente, Tourneen.
 * Die Vorgaben werden hier gepflegt, abschnittsweise; der Rest wird hier
 * gelesen und führt in sein Modul.
 */
@Component({
  selector: 'souffleur-produktionen',
  imports: [
    CurrencyPipe,
    DatePipe,
    FormsModule,
    RouterLink,
    AktivitaetComponent,
    LesezeichenComponent,
    DialogComponent,
    EventDialogComponent,
    IconComponent,
    MeldungComponent,
    MetricStripComponent,
  ],
  templateUrl: './produktionen.component.html',
  host: {
    class: 'block h-full min-h-0',
    '(document:keydown)': 'blaettern($event)',
  },
})
export class ProduktionenComponent implements OnInit {
  private readonly productions = inject(ProductionsService);
  private readonly tasks = inject(TasksService);
  private readonly documents = inject(DocumentsService);
  private readonly vouchers = inject(VouchersService);
  private readonly resourceList = inject(ResourcesService);
  private readonly catalogs = inject(CatalogsService);
  private readonly peopleList = inject(PeopleService);
  private readonly modules = inject(ModulesService);
  private readonly actions = inject(ActionsRegistry);
  private readonly auth = inject(AuthService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly list = signal<Production[]>([]);
  protected readonly statuses = signal<ProductionStatusInfo[]>([]);
  protected readonly functions = signal<CatalogEntry[]>([]);
  protected readonly people = signal<Person[]>([]);

  /** Was der Termindialog braucht -- erst geladen, wenn jemand das Plus drückt. */
  protected readonly eventCategories = signal<CatalogEntry[]>([]);
  protected readonly resources = signal<Resource[]>([]);

  protected readonly loading = signal(true);
  protected readonly blocked = signal(false);
  protected readonly busy = signal(false);
  protected readonly problems = signal<string[]>([]);
  protected readonly includeArchived = signal(false);

  /** Welche Produktion aufgeschlagen ist -- und was zu ihr geladen wurde. */
  protected readonly selected = signal<string | null>(null);
  protected readonly overview = signal<ProductionOverview | null>(null);
  protected readonly overviewLoading = signal(false);

  protected readonly index = computed(() => this.list().findIndex((p) => p.id === this.selected()));
  protected readonly current = computed<Production | null>(() => {
    const loaded = this.overview();
    const inList = this.list()[this.index()] ?? null;
    // Die Übersicht trägt die frischeren Listen; die Zeile die frischeren Zahlen.
    return loaded && inList && loaded.production.id === inList.id
      ? { ...loaded.production, figures: inList.figures ?? loaded.production.figures }
      : inList;
  });

  protected readonly editing = signal<Section | null>(null);
  protected readonly draft = signal<ProductionDraft>(emptyDraft());
  protected readonly rolesDraft = signal<ProductionRoleInput[]>([]);
  protected readonly budgetDraft = signal<BudgetLineDraft[]>([]);
  protected readonly needsDraft = signal<ProductionNeedInput[]>([]);
  protected readonly newTask = signal('');
  protected readonly taskOpen = signal(false);
  protected readonly voucherDraft = signal<VoucherDraft>(emptyVoucher());
  private readonly taskField = viewChild<ElementRef<HTMLInputElement>>('aufgabenfeld');
  private readonly fileField = viewChild<ElementRef<HTMLInputElement>>('dateifeld');

  protected readonly canManage = computed(() => this.auth.can('ProductionsManage'));
  protected readonly canSeeFinance = computed(() => this.auth.can('FinanceRead'));
  protected readonly canBook = computed(() => this.auth.can('FinanceBook'));
  protected readonly canPlan = computed(() => this.auth.can('CalendarPlan'));
  protected readonly canFile = computed(() => this.auth.can('DocumentsWrite'));
  protected readonly directions = [
    { value: VoucherDirection.Incoming, label: 'Eingang (Kosten)' },
    { value: VoucherDirection.Outgoing, label: 'Ausgang (Einnahme)' },
  ];
  protected readonly today = new Date().toISOString().slice(0, 10);
  protected readonly touringOn = computed(() => this.modules.isEnabled('Touring'));

  protected readonly status = ProductionStatus;
  protected readonly kinds = ProductionNeedKind;
  protected readonly categories = BUDGET_CATEGORIES;

  /** Die Summe der Positionen im Bogen, live beim Tippen. */
  protected readonly budgetDraftTotal = computed(() =>
    this.budgetDraft().reduce((sum, line) => sum + (parseAmount(line.amount) ?? 0), 0),
  );

  async ngOnInit(): Promise<void> {
    this.actions.set(
      'Produktionen',
      this.canManage()
        ? [
            {
              label: 'Produktion anlegen',
              icon: 'produktion',
              primary: true,
              run: () => this.startNew(),
            },
          ]
        : [],
      [
        { label: 'Aufgaben', icon: 'aufgaben', path: '/aufgaben' },
        { label: 'Kalender', icon: 'kalender', path: '/kalender' },
        { label: 'Dokumente', icon: 'dokumente', path: '/dokumente' },
        ...(this.touringOn()
          ? [{ label: 'Touring', icon: 'touring' as const, path: '/touring' }]
          : []),
      ],
    );

    try {
      this.statuses.set(await this.productions.statuses());
    } catch {
      // 404 heißt: Das Modul ist in diesem Haus abgeschaltet.
      this.blocked.set(true);
      this.loading.set(false);
      return;
    }

    await Promise.all([this.reload(), this.loadLists()]);
    this.loading.set(false);

    if (this.route.snapshot.queryParamMap.get('neu')) {
      this.startNew();
    }

    // Aus der Suche oder vom Blättern: `?produktion=<id>` schlägt die Seite auf.
    verfolgeAdresse(this.route, this.destroyRef, async (params) => {
      const id = params.get('produktion');
      if (!id) {
        if (this.selected() === null && this.list().length > 0) {
          await this.show(this.list()[0].id, false);
        }
        return;
      }
      if (id === this.selected()) {
        return;
      }
      if (!this.list().some((p) => p.id === id) && !this.includeArchived()) {
        this.includeArchived.set(true);
        await this.reload();
      }
      await this.show(id, false);
    });
  }

  protected async reload(): Promise<void> {
    try {
      this.list.set(await this.productions.list(this.includeArchived()));
      // Die aufgeschlagene Seite ist aus der Liste gefallen? Dann die erste.
      if (this.selected() !== null && this.index() < 0) {
        const first = this.list()[0];
        if (first) {
          await this.show(first.id);
        } else {
          this.selected.set(null);
          this.overview.set(null);
        }
      }
    } catch (error) {
      this.problems.set(messagesFrom(error));
    }
  }

  private async loadLists(): Promise<void> {
    // Was fehlt, fehlt still: Ohne Kontaktrecht gibt es keine Personen zur
    // Auswahl, aber das Buch bleibt lesbar.
    const [functions, people] = await Promise.all([
      this.catalogs.entries('ParticipationRole').catch(() => []),
      this.peopleList.load().catch(() => []),
    ]);
    this.functions.set(functions);
    this.people.set(people);
  }

  // --- Blättern ---

  protected async show(id: string, writeUrl = true): Promise<void> {
    this.selected.set(id);
    this.overview.set(null);
    this.overviewLoading.set(true);
    this.problems.set([]);

    if (writeUrl) {
      void this.router.navigate([], {
        relativeTo: this.route,
        queryParams: { produktion: id },
        replaceUrl: true,
      });
    }

    try {
      const loaded = await this.productions.overview(id);
      // Inzwischen weitergeblättert? Dann gehört das Ergebnis nicht mehr hierher.
      if (this.selected() === id) {
        this.overview.set(loaded);
      }
    } catch (error) {
      this.problems.set(messagesFrom(error));
    } finally {
      this.overviewLoading.set(false);
    }
  }

  protected async step(by: number): Promise<void> {
    const target = this.list()[this.index() + by];
    if (target) {
      await this.show(target.id);
    }
  }

  /** Pfeil links und rechts blättern -- außer jemand tippt gerade. */
  protected blaettern(event: KeyboardEvent): void {
    if (this.editing() !== null || event.altKey || event.ctrlKey || event.metaKey) {
      return;
    }
    const target = event.target as HTMLElement | null;
    if (target && ['INPUT', 'TEXTAREA', 'SELECT'].includes(target.tagName)) {
      return;
    }
    if (event.key === 'ArrowRight') {
      event.preventDefault();
      void this.step(1);
    } else if (event.key === 'ArrowLeft') {
      event.preventDefault();
      void this.step(-1);
    }
  }

  /** Nach dem Speichern: Liste und Seite frisch, die Seite bleibt aufgeschlagen. */
  private async refresh(id: string): Promise<void> {
    await this.reload();
    await this.show(id, false);
  }

  // --- Stammdaten ---

  protected startNew(): void {
    this.problems.set([]);
    this.draft.set(emptyDraft());
    this.editing.set('neu');
  }

  protected startEdit(production: Production): void {
    this.problems.set([]);
    this.draft.set({
      title: production.title,
      subtitle: production.subtitle ?? '',
      status: production.status,
      premiereDate: production.premiereDate ?? '',
      closingDate: production.closingDate ?? '',
      budget: production.budget === null ? '' : String(production.budget),
      description: production.description ?? '',
      research: production.research ?? '',
      notes: production.notes ?? '',
    });
    this.editing.set('stammdaten');
  }

  protected patch(change: Partial<ProductionDraft>): void {
    this.draft.update((current) => ({ ...current, ...change }));
  }

  protected async save(): Promise<void> {
    const draft = this.draft();
    if (!draft.title.trim()) {
      this.problems.set(['Eine Produktion braucht einen Titel.']);
      return;
    }

    const budget = draft.budget.trim() === '' ? null : parseAmount(draft.budget);
    if (budget !== null && budget === undefined) {
      this.problems.set(['Das Budget ist keine Zahl.']);
      return;
    }

    const input = {
      title: draft.title.trim(),
      subtitle: draft.subtitle.trim() || null,
      status: draft.status,
      premiereDate: draft.premiereDate || null,
      closingDate: draft.closingDate || null,
      budget: budget ?? null,
      description: draft.description.trim() || null,
      research: draft.research.trim() || null,
      notes: draft.notes.trim() || null,
    };

    await this.submit(async () => {
      const id = this.editing() === 'neu' ? null : this.selected();
      const saved = id
        ? await this.productions.update(id, input)
        : await this.productions.create(input);
      if (saved.status === ProductionStatus.Archived && !this.includeArchived()) {
        this.includeArchived.set(true);
      }
      return saved.id;
    });
  }

  /** Erst fragen: Löschen ist endgültig, und meist ist Abspielen gemeint. */
  protected startRemove(): void {
    this.problems.set([]);
    this.editing.set('loeschen');
  }

  protected async remove(production: Production): Promise<void> {
    this.problems.set([]);
    try {
      await this.productions.remove(production.id);
      this.editing.set(null);
      this.selected.set(null);
      this.overview.set(null);
      await this.reload();
      const first = this.list()[0];
      if (first) {
        await this.show(first.id);
      }
    } catch (error) {
      // Haengt noch etwas dran, bleibt das Fenster offen und sagt, warum.
      this.problems.set(messagesFrom(error));
    }
  }

  /** Abspielen statt löschen: Die Produktion bleibt mit allem, was an ihr hängt. */
  protected async archive(production: Production): Promise<void> {
    await this.submit(async () => {
      await this.productions.update(production.id, {
        title: production.title,
        subtitle: production.subtitle,
        status: ProductionStatus.Archived,
        premiereDate: production.premiereDate,
        closingDate: production.closingDate,
        budget: production.budget,
        description: production.description,
        research: production.research,
        notes: production.notes,
      });
      if (!this.includeArchived()) {
        this.includeArchived.set(true);
      }
      return production.id;
    });
  }

  protected cancel(): void {
    this.editing.set(null);
    this.problems.set([]);
  }

  // --- Besetzung ---

  protected startRoles(production: Production): void {
    this.problems.set([]);
    this.rolesDraft.set(
      production.roles.map((r) => ({
        name: r.name,
        functionId: r.functionId,
        contactId: r.contactId,
        note: r.note ?? '',
      })),
    );
    this.editing.set('besetzung');
  }

  /** Das Plus am Abschnitt: der Bogen, gleich mit einer leeren Zeile. */
  protected plusRole(production: Production): void {
    this.startRoles(production);
    this.addRole();
  }

  protected plusBudgetLine(production: Production): void {
    this.startBudget(production);
    this.addBudgetLine();
  }

  protected plusNeed(production: Production, kind: number): void {
    this.startNeeds(production, kind);
    this.addNeed(kind);
  }

  protected addRole(): void {
    this.rolesDraft.update((rows) => [
      ...rows,
      { name: '', functionId: null, contactId: null, note: '' },
    ]);
  }

  protected patchRole(index: number, change: Partial<ProductionRoleInput>): void {
    this.rolesDraft.update((rows) => rows.map((r, i) => (i === index ? { ...r, ...change } : r)));
  }

  protected removeRole(index: number): void {
    this.rolesDraft.update((rows) => rows.filter((_, i) => i !== index));
  }

  protected async saveRoles(): Promise<void> {
    const rows = this.rolesDraft();
    if (rows.some((r) => !r.name.trim())) {
      this.problems.set(['Jede Rolle braucht einen Namen.']);
      return;
    }
    await this.submit(async () => {
      const id = this.selected()!;
      await this.productions.saveRoles(
        id,
        rows.map((r) => ({
          name: r.name.trim(),
          functionId: r.functionId || null,
          contactId: r.contactId || null,
          note: r.note?.trim() || null,
        })),
      );
      return id;
    });
  }

  // --- Budget ---

  protected startBudget(production: Production): void {
    this.problems.set([]);
    this.budgetDraft.set(
      production.budgetLines.map((l) => ({
        title: l.title,
        category: l.category ?? '',
        amount: String(l.amount),
        note: l.note ?? '',
      })),
    );
    this.editing.set('budget');
  }

  protected addBudgetLine(): void {
    this.budgetDraft.update((rows) => [...rows, { title: '', category: '', amount: '', note: '' }]);
  }

  protected patchBudgetLine(index: number, change: Partial<BudgetLineDraft>): void {
    this.budgetDraft.update((rows) => rows.map((l, i) => (i === index ? { ...l, ...change } : l)));
  }

  protected removeBudgetLine(index: number): void {
    this.budgetDraft.update((rows) => rows.filter((_, i) => i !== index));
  }

  protected async saveBudget(): Promise<void> {
    const rows = this.budgetDraft();
    if (rows.some((l) => !l.title.trim())) {
      this.problems.set(['Jede Budgetposition braucht eine Bezeichnung.']);
      return;
    }
    const lines: ProductionBudgetLineInput[] = [];
    for (const row of rows) {
      const amount = parseAmount(row.amount);
      if (amount === undefined) {
        this.problems.set([`Der Betrag bei „${row.title.trim()}“ ist keine Zahl.`]);
        return;
      }
      lines.push({
        title: row.title.trim(),
        category: row.category.trim() || null,
        amount: amount ?? 0,
        note: row.note.trim() || null,
      });
    }
    await this.submit(async () => {
      const id = this.selected()!;
      await this.productions.saveBudget(id, lines);
      return id;
    });
  }

  // --- Bedarf und Papiere ---

  protected startNeeds(production: Production, kind: number): void {
    this.problems.set([]);
    this.needsDraft.set(
      production.needs
        .filter((n) => n.kind === kind)
        .map((n) => ({
          title: n.title,
          kind: n.kind,
          quantity: n.quantity ?? '',
          isDone: n.isDone,
          note: n.note ?? '',
        })),
    );
    this.editing.set(kind === ProductionNeedKind.Document ? 'papiere' : 'bedarf');
  }

  protected addNeed(kind: number): void {
    this.needsDraft.update((rows) => [
      ...rows,
      { title: '', kind, quantity: '', isDone: false, note: '' },
    ]);
  }

  protected patchNeed(index: number, change: Partial<ProductionNeedInput>): void {
    this.needsDraft.update((rows) => rows.map((n, i) => (i === index ? { ...n, ...change } : n)));
  }

  protected removeNeed(index: number): void {
    this.needsDraft.update((rows) => rows.filter((_, i) => i !== index));
  }

  /**
   * Speichert die eine Art und lässt die andere stehen: Wer die Papiere
   * pflegt, schickt den Bedarf unverändert mit, weil der Server die Liste
   * als Ganzes ersetzt.
   */
  protected async saveNeeds(): Promise<void> {
    const production = this.current();
    if (!production) {
      return;
    }
    const kind =
      this.editing() === 'papiere' ? ProductionNeedKind.Document : ProductionNeedKind.Material;
    const rows = this.needsDraft();
    if (rows.some((n) => !n.title.trim())) {
      this.problems.set([
        kind === ProductionNeedKind.Document
          ? 'Jedes Papier braucht eine Bezeichnung.'
          : 'Jeder Bedarf braucht eine Bezeichnung.',
      ]);
      return;
    }

    const others: ProductionNeedInput[] = production.needs
      .filter((n) => n.kind !== kind)
      .map((n) => ({
        title: n.title,
        kind: n.kind,
        quantity: n.quantity,
        isDone: n.isDone,
        note: n.note,
      }));
    const mine: ProductionNeedInput[] = rows.map((n) => ({
      title: n.title.trim(),
      kind,
      quantity: n.quantity?.trim() || null,
      isDone: n.isDone,
      note: n.note?.trim() || null,
    }));

    await this.submit(async () => {
      await this.productions.saveNeeds(production.id, [...others, ...mine]);
      return production.id;
    });
  }

  /** Ein Häkchen direkt im Buch -- ohne den Bogen zu öffnen. */
  protected async toggleNeed(production: Production, needId: string): Promise<void> {
    if (!this.canManage()) {
      return;
    }
    const needs: ProductionNeedInput[] = production.needs.map((n) => ({
      title: n.title,
      kind: n.kind,
      quantity: n.quantity,
      isDone: n.id === needId ? !n.isDone : n.isDone,
      note: n.note,
    }));
    this.problems.set([]);
    try {
      await this.productions.saveNeeds(production.id, needs);
      await this.refresh(production.id);
    } catch (error) {
      this.problems.set(messagesFrom(error));
    }
  }

  // --- Aufgaben ---

  /** Das Plus: das Feld erscheint, der Cursor steht drin. */
  protected plusTask(): void {
    this.taskOpen.set(true);
    setTimeout(() => this.taskField()?.nativeElement.focus());
  }

  /** Eine Aufgabe aus dem Buch heraus -- nur der Titel, der Rest im Aufgabenmodul. */
  protected async addTask(production: Production): Promise<void> {
    const title = this.newTask().trim();
    if (!title) {
      this.taskOpen.set(false);
      return;
    }
    this.busy.set(true);
    this.problems.set([]);
    try {
      await this.tasks.create({ title, productionId: production.id });
      this.newTask.set('');
      this.taskOpen.set(false);
      await this.refresh(production.id);
    } catch (error) {
      this.problems.set(messagesFrom(error));
    } finally {
      this.busy.set(false);
    }
  }

  // --- Termin ---

  /** Der volle Termindialog des Kalenders, mit der Produktion im Feld. */
  protected async plusEvent(): Promise<void> {
    this.problems.set([]);
    if (this.eventCategories().length === 0) {
      const [categories, resources] = await Promise.all([
        this.catalogs.entries('EventCategory').catch(() => []),
        this.resourceList.load().catch(() => []),
      ]);
      this.eventCategories.set(categories);
      this.resources.set(resources);
    }
    this.editing.set('termin');
  }

  protected async afterEvent(): Promise<void> {
    this.editing.set(null);
    const id = this.selected();
    if (id) {
      await this.refresh(id);
    }
  }

  // --- Dokument ---

  /** Das Plus öffnet den Dateidialog; die Datei landet in der Ablage, an der Produktion. */
  protected plusDocument(): void {
    this.fileField()?.nativeElement.click();
  }

  protected async fileChosen(production: Production, event: Event): Promise<void> {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = '';
    if (!file) {
      return;
    }
    this.busy.set(true);
    this.problems.set([]);
    try {
      const stored = await this.documents.upload(file, {
        name: null,
        note: null,
        tags: [],
        restricted: false,
        groupIds: [],
      });
      await this.documents.link(stored.id, DocumentTarget.Production, production.id);
      await this.refresh(production.id);
    } catch (error) {
      this.problems.set(messagesFrom(error));
    } finally {
      this.busy.set(false);
    }
  }

  // --- Beleg ---

  protected plusVoucher(): void {
    this.problems.set([]);
    this.voucherDraft.set(emptyVoucher());
    this.editing.set('beleg');
  }

  protected patchVoucher(change: Partial<VoucherDraft>): void {
    this.voucherDraft.update((current) => ({ ...current, ...change }));
  }

  protected voucherFile(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.patchVoucher({ file: input.files?.[0] ?? null });
  }

  /** Das Nötigste zum Beleg; Zuordnung zur Produktion ist gesetzt. Mehr im Belegbogen. */
  protected async saveVoucher(): Promise<void> {
    const draft = this.voucherDraft();
    const amount = parseAmount(draft.grossAmount);
    if (!draft.description.trim()) {
      this.problems.set(['Ein Beleg braucht eine Beschreibung.']);
      return;
    }
    if (amount === null || amount === undefined) {
      this.problems.set(['Der Betrag ist keine Zahl.']);
      return;
    }
    await this.submit(async () => {
      const id = this.selected()!;
      await this.vouchers.upload(
        {
          date: draft.date,
          fiscalYear: draft.date.slice(0, 4),
          direction: String(draft.direction),
          // Ohne Datei ist es ein Papierbeleg, der im Ordner liegt.
          source: String(draft.file ? VoucherSource.Upload : VoucherSource.PaperOnly),
          grossAmount: String(amount),
          description: draft.description.trim(),
          productionId: id,
        },
        draft.file,
      );
      return id;
    });
  }

  // --- gemeinsam ---

  private async submit(work: () => Promise<string>): Promise<void> {
    this.busy.set(true);
    this.problems.set([]);
    try {
      const id = await work();
      this.editing.set(null);
      await this.refresh(id);
    } catch (error) {
      this.problems.set(messagesFrom(error));
    } finally {
      this.busy.set(false);
    }
  }

  // --- Anzeige ---

  /** Premiere und Derniere als ein Satz. */
  protected dates(production: Production): string {
    if (!production.premiereDate) {
      return 'noch ohne Premiere';
    }

    const from = `Premiere ${day(production.premiereDate)}`;
    return production.closingDate ? `${from} – Derniere ${day(production.closingDate)}` : from;
  }

  protected roles(production: Production, cast: boolean) {
    return production.roles.filter((r) => r.isCast === cast);
  }

  protected needs(production: Production, kind: number) {
    return production.needs.filter((n) => n.kind === kind);
  }

  protected planned(production: Production): number {
    return production.budgetLines.reduce((sum, l) => sum + l.amount, 0);
  }

  /** Anteil am Budget in Prozent -- gedeckelt, damit der Balken nicht ausbricht. */
  protected share(production: Production, amount: number | null | undefined): number {
    if (!production.budget || production.budget <= 0 || !amount) {
      return 0;
    }
    return Math.min(100, Math.round((amount / production.budget) * 100));
  }

  protected overBudget(production: Production): boolean {
    const costs = production.figures?.costs ?? 0;
    return production.budget !== null && production.budget > 0 && costs > production.budget;
  }

  protected overPlanned(production: Production): boolean {
    return (
      production.budget !== null &&
      production.budget > 0 &&
      this.planned(production) > production.budget
    );
  }

  /** Die kommenden Termine zuerst -- und nicht mehr als eine Handvoll. */
  protected upcoming(overview: ProductionOverview): ProductionOverview['events'] {
    const now = Date.now();
    return overview.events
      .filter((e) => new Date(e.endsAt).getTime() >= now && e.status !== 3)
      .slice(0, 8);
  }

  protected past(overview: ProductionOverview): number {
    const now = Date.now();
    return overview.events.filter((e) => new Date(e.endsAt).getTime() < now).length;
  }

  protected localDate(iso: string): string {
    const date = new Date(iso);
    const pad = (n: number) => String(n).padStart(2, '0');
    return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
  }
}

function emptyVoucher(): VoucherDraft {
  return {
    date: new Date().toISOString().slice(0, 10),
    direction: VoucherDirection.Incoming,
    grossAmount: '',
    description: '',
    file: null,
  };
}

function emptyDraft(): ProductionDraft {
  return {
    title: '',
    subtitle: '',
    status: ProductionStatus.Planned,
    premiereDate: '',
    closingDate: '',
    budget: '',
    description: '',
    research: '',
    notes: '',
  };
}

/** „1.200,50" und „1200.50" sind dieselbe Zahl; leer ist null, Unsinn ist undefined. */
function parseAmount(text: string): number | null | undefined {
  const trimmed = text.trim();
  if (trimmed === '') {
    return null;
  }
  const normalized =
    trimmed.includes(',') && trimmed.includes('.')
      ? trimmed.replace(/\./g, '').replace(',', '.')
      : trimmed.replace(',', '.');
  const value = Number(normalized);
  return Number.isNaN(value) ? undefined : value;
}

function day(iso: string): string {
  return new Date(`${iso}T12:00:00Z`).toLocaleDateString('de-DE', {
    day: 'numeric',
    month: 'short',
    year: 'numeric',
  });
}
