import { DestroyRef, Component, OnInit, computed, effect, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { verfolgeAdresse } from '../../shared/sprungziel';
import {
  CalendarService,
  CalendarSource,
  Occurrence,
  addDays,
  groupByDate,
  isoDate,
} from '../../core/calendar.service';
import { CatalogEntry, CatalogsService } from '../../core/catalogs.service';
import { Person, PeopleService } from '../../core/people.service';
import { Production, ProductionsService } from '../../core/productions.service';
import { Resource, ResourcesService } from '../../core/resources.service';
import { ActionsRegistry } from '../../shell/actions.registry';
import { DialogComponent } from '../../shared/dialog.component';
import { MetricStripComponent } from '../../shared/metric-strip.component';
import { EventDialogComponent, isEditable } from './event-dialog.component';
import { AuthService } from '../../core/auth.service';
import { MonthViewComponent } from './month-view.component';
import { TimeGridComponent } from './time-grid.component';
import { YearGridComponent } from './year-grid.component';

export type CalendarView = 'year' | 'month' | 'week' | 'day';

/** Wie viele Monate die Jahresansicht zeigt. Der Server deckelt bei rund 26. */
const SPANS = [3, 6, 9, 12, 18, 24];

const MONTHS = [
  'Januar',
  'Februar',
  'März',
  'April',
  'Mai',
  'Juni',
  'Juli',
  'August',
  'September',
  'Oktober',
  'November',
  'Dezember',
];

/**
 * Der Kalender mit seinen vier Ansichten.
 *
 * Leitansicht ist das Jahresraster (ADR-0006) -- es beantwortet die Fragen, die
 * in keinen Monat passen. Monat, Woche und Tag stehen daneben, weil sie andere
 * Fragen beantworten: Das Jahr zeigt die Verteilung, die Woche die Dauer und
 * die Überschneidung.
 *
 * Alle vier teilen sich einen Ankertag und dieselben Daten. Wer die Ansicht
 * wechselt, bleibt deshalb dort, wo er war.
 */
@Component({
  selector: 'souffleur-calendar',
  imports: [
    FormsModule,
    RouterLink,
    YearGridComponent,
    MonthViewComponent,
    TimeGridComponent,
    EventDialogComponent,
    DialogComponent,
    MetricStripComponent,
  ],
  templateUrl: './calendar.component.html',

  // Der Wirt eines Angular-Bausteins ist von sich aus inline und ohne Höhe.
  // Ohne das greift das h-full in der Vorlage ins Leere.
  host: { class: 'block h-full min-h-0' },
})
export class CalendarComponent implements OnInit {
  private readonly calendar = inject(CalendarService);
  private readonly route = inject(ActivatedRoute);
  private readonly destroyRef = inject(DestroyRef);
  private readonly catalogs = inject(CatalogsService);
  private readonly resourceList = inject(ResourcesService);
  private readonly peopleList = inject(PeopleService);
  private readonly productionList = inject(ProductionsService);
  private readonly actions = inject(ActionsRegistry);
  private readonly auth = inject(AuthService);

  /** Planen darf, wer das Recht hat -- sonst steht kein Knopf da, der nur abgewiesen würde. */
  protected readonly canPlan = computed(() => this.auth.can('CalendarPlan'));

  /** Der Termin, der gerade im Bogen bearbeitet wird. */
  protected readonly editing = signal<Occurrence | null>(null);

  protected readonly spans = SPANS;
  protected readonly loading = this.calendar.loading;

  protected readonly views: { key: CalendarView; label: string }[] = [
    { key: 'year', label: 'Jahr' },
    { key: 'month', label: 'Monat' },
    { key: 'week', label: 'Woche' },
    { key: 'day', label: 'Tag' },
  ];

  protected readonly view = signal<CalendarView>('year');

  /** Der Tag, um den herum jede Ansicht ihren Ausschnitt bildet. */
  protected readonly anchor = signal(today());

  protected readonly monthCount = signal(12);

  protected readonly categories = signal<CatalogEntry[]>([]);
  protected readonly hiddenCategories = signal<ReadonlySet<string>>(new Set());

  protected readonly resources = signal<Resource[]>([]);

  /**
   * Umgekehrt zu den Kategorien: Dort wird weggeklickt, hier ausgewählt.
   * „Was ist auf der Probebühne los?" ist die Frage, die man an Räume hat --
   * nicht „zeig mir alles außer der Probebühne".
   */
  protected readonly pickedResources = signal<ReadonlySet<string>>(new Set());

  protected readonly people = signal<Person[]>([]);
  protected readonly functions = signal<CatalogEntry[]>([]);
  protected readonly productions = signal<Production[]>([]);

  /**
   * Eine Person statt mehrerer: „Wann hat Clara Dienst?" ist die Frage, die
   * man an einen Menschen hat -- nicht „zeig mir Clara und Ben zusammen".
   */
  protected readonly pickedPerson = signal<string>('');

  protected readonly selectedDay = signal<string | null>(null);
  protected readonly dialogDate = signal<string | null>(null);

  protected readonly anchorYear = computed(() => Number(this.anchor().slice(0, 4)));
  protected readonly anchorMonth = computed(() => Number(this.anchor().slice(5, 7)));

  /** Der Zeitraum, den der Server liefern soll -- je Ansicht ein anderer. */
  protected readonly range = computed<{ from: string; to: string }>(() => {
    const year = this.anchorYear();
    const month = this.anchorMonth();

    switch (this.view()) {
      case 'year': {
        const last = addMonths(year, month, this.monthCount() - 1);
        return {
          from: isoDate(year, month, 1),
          to: isoDate(last.year, last.month, daysInMonth(last.year, last.month)),
        };
      }

      case 'month': {
        // Die Monatsansicht zeigt ganze Wochen, also auch die Randtage aus
        // den Nachbarmonaten -- sonst blieben sie leer, obwohl dort etwas ist.
        const first = mondayOf(isoDate(year, month, 1));
        return { from: first, to: addDays(first, 41) };
      }

      case 'week': {
        const monday = mondayOf(this.anchor());
        return { from: monday, to: addDays(monday, 6) };
      }

      default:
        return { from: this.anchor(), to: this.anchor() };
    }
  });

  protected readonly weekDays = computed(() => {
    const monday = mondayOf(this.anchor());
    return Array.from({ length: 7 }, (_, index) => addDays(monday, index));
  });

  protected readonly shown = computed(() => {
    const hidden = this.hiddenCategories();
    const picked = this.pickedResources();

    return this.calendar.occurrences().filter((o) => {
      if (hidden.size > 0 && o.categoryId && hidden.has(o.categoryId)) {
        return false;
      }

      if (picked.size > 0 && !o.resources.some((resource) => picked.has(resource.id))) {
        return false;
      }

      const person = this.pickedPerson();
      return !person || o.participants.some((p) => p.contactId === person);
    });
  });

  protected readonly byDate = computed(() => groupByDate(this.shown()));

  protected readonly dayEntries = computed<readonly Occurrence[]>(() => {
    const day = this.selectedDay();
    return day ? (this.byDate().get(day) ?? []) : [];
  });

  /** Die Zeile unter der Überschrift des Tagesfensters -- die Zählung, nicht die Liste. */
  protected readonly dayNote = computed(() => {
    const entries = this.dayEntries();
    if (entries.length === 0) {
      return '';
    }

    const cancelled = entries.filter((o) => o.status === 3).length;
    const count = entries.length === 1 ? '1 Termin' : `${entries.length} Termine`;
    return cancelled > 0 ? `${count}, davon ${cancelled} abgesagt` : count;
  });

  protected readonly label = computed(() => {
    const year = this.anchorYear();
    const month = this.anchorMonth();

    switch (this.view()) {
      case 'year': {
        const last = addMonths(year, month, this.monthCount() - 1);
        return `${MONTHS[month - 1]} ${year} – ${MONTHS[last.month - 1]} ${last.year}`;
      }

      case 'month':
        return `${MONTHS[month - 1]} ${year}`;

      case 'week': {
        const monday = mondayOf(this.anchor());
        const sunday = addDays(monday, 6);
        return `KW ${isoWeek(monday)} · ${shortDate(monday)} – ${longDate(sunday)}`;
      }

      default:
        return new Date(`${this.anchor()}T12:00:00Z`).toLocaleDateString('de-DE', {
          weekday: 'long',
          day: 'numeric',
          month: 'long',
          year: 'numeric',
        });
    }
  });

  protected readonly summary = computed(() => {
    const entries = this.shown();
    return {
      total: entries.length,
      cancelled: entries.filter((o) => o.status === 3).length,
      planned: entries.filter((o) => o.status === 1).length,
    };
  });

  constructor() {
    // Der Zeitraum steuert, was geladen wird. Als Effekt statt im Klick-Handler,
    // damit jeder Weg dorthin -- Ansichtswechsel, Blättern, Schnellwahl --
    // dasselbe tut.
    effect(() => {
      const { from, to } = this.range();
      void this.calendar.range(from, to);
    });
  }

  async ngOnInit(): Promise<void> {
    this.actions.set(
      'Kalender',
      [
        {
          label: 'Termin anlegen',
          icon: 'kalender',
          primary: true,
          run: () => this.openDialog(this.selectedDay() ?? this.anchor()),
        },
      ],
      [
        { label: 'Terminkategorien', icon: 'etikett', path: '/einstellungen/listen/EventCategory' },
        { label: 'Ressourcen', icon: 'ressource', path: '/einstellungen/ressourcen' },
        { label: 'Kalender abonnieren', icon: 'abo', path: '/einstellungen/kalender-abos' },
      ],
    );

    this.categories.set(await this.catalogs.entries('EventCategory'));
    this.resources.set(await this.resourceList.load());
    this.people.set(await this.peopleList.load());
    this.functions.set(await this.catalogs.entries('ParticipationRole'));
    this.productions.set(await this.productionList.choices());

    // Wer von der Übersicht kommt, will den Bogen schon offen sehen.
    if (this.route.snapshot.queryParamMap.get('neu')) {
      this.openDialog(this.selectedDay() ?? this.anchor());
    }

    // Aus der Suche: `?tag=JJJJ-MM-TT` zeigt den Tag -- in seinem Umfeld,
    // nicht als einzelnen Datensatz. Der Termin selbst steht dann im
    // Tagesfenster, zwischen dem, was sonst noch an dem Tag ist.
    verfolgeAdresse(this.route, this.destroyRef, (params) => {
      const tag = params.get('tag');
      if (tag && /^\d{4}-\d{2}-\d{2}$/.test(tag)) {
        this.zeigeTag(tag);
      }
    });
  }

  /** Blättert nur, wenn der Tag nicht ohnehin im Ausschnitt liegt. */
  private zeigeTag(tag: string): void {
    const { from, to } = this.range();
    if (tag < from || tag > to) {
      const tagesgenau = this.view() === 'week' || this.view() === 'day';
      this.anchor.set(tagesgenau ? tag : tag.slice(0, 8) + '01');
    }
    this.selectedDay.set(tag);
  }

  protected setView(view: CalendarView): void {
    this.view.set(view);

    // Ein offenes Tagesfenster wird in Woche und Tag zum Ausschnitt -- dort
    // zeigt die Ansicht den Tag selbst, das Fenster darüber wäre doppelt.
    const day = this.selectedDay();
    if (day && (view === 'week' || view === 'day')) {
      this.anchor.set(day);
      this.selectedDay.set(null);
    }
  }

  /** Aus dem Tagesfenster in die Tagesansicht: derselbe Tag, nur als Ausschnitt. */
  protected openInDay(day: string): void {
    this.anchor.set(day);
    this.view.set('day');
    this.selectedDay.set(null);
  }

  protected setSpan(months: number): void {
    this.monthCount.set(months);
  }

  /** Blättert um eine Einheit der jeweiligen Ansicht. */
  protected shift(direction: number): void {
    const year = this.anchorYear();
    const month = this.anchorMonth();

    switch (this.view()) {
      case 'year':
      case 'month': {
        const next = addMonths(year, month, direction);
        this.anchor.set(isoDate(next.year, next.month, 1));
        break;
      }

      case 'week':
        this.anchor.set(addDays(this.anchor(), direction * 7));
        break;

      default:
        this.anchor.set(addDays(this.anchor(), direction));
    }
  }

  protected goToday(): void {
    this.anchor.set(today());
    this.selectedDay.set(null);
  }

  /** Die Spielzeit läuft von August bis Juli, nicht von Januar bis Dezember. */
  protected season(): void {
    const now = new Date();
    const year = now.getMonth() + 1 >= 8 ? now.getFullYear() : now.getFullYear() - 1;
    this.view.set('year');
    this.anchor.set(isoDate(year, 8, 1));
    this.monthCount.set(12);
  }

  protected calendarYear(): void {
    this.view.set('year');
    this.anchor.set(isoDate(new Date().getFullYear(), 1, 1));
    this.monthCount.set(12);
  }

  protected toggleCategory(id: string): void {
    this.hiddenCategories.update((current) => {
      const next = new Set(current);
      if (!next.delete(id)) {
        next.add(id);
      }
      return next;
    });
  }

  protected isHidden(id: string): boolean {
    return this.hiddenCategories().has(id);
  }

  protected toggleResource(id: string): void {
    this.pickedResources.update((current) => {
      const next = new Set(current);
      if (!next.delete(id)) {
        next.add(id);
      }
      return next;
    });
  }

  protected isPicked(id: string): boolean {
    return this.pickedResources().has(id);
  }

  /** Ob der Eintrag aus den Aufgaben kommt und hier nur gelesen wird. */
  protected isDeadline(entry: Occurrence): boolean {
    return entry.source === CalendarSource.Tasks;
  }

  /** Ob der Eintrag ein Gastspiel ist -- geändert wird im Touring. */
  protected isTouring(entry: Occurrence): boolean {
    return entry.source === CalendarSource.Touring;
  }

  /** Ob der Eintrag eine Abwesenheit ist -- beantragt wird im Personal. */
  protected isAbsence(entry: Occurrence): boolean {
    return entry.source === CalendarSource.Absence;
  }

  /** Wer eingeteilt ist, als ein Satz -- für das Tagesfenster. */
  protected assigned(entry: Occurrence): string {
    return entry.participants
      .map((person) => (person.function ? `${person.name} (${person.function})` : person.name))
      .join(' · ');
  }

  protected clearResources(): void {
    this.pickedResources.set(new Set());
  }

  /**
   * Die Farbe der Ressourcenart als Hintergrund, stark verdünnt -- gemischt
   * statt mit Deckkraft gesetzt, damit die Schrift darüber deckend bleibt.
   */
  protected wash(color: string | null): string {
    return color ? `color-mix(in oklab, ${color} 16%, transparent)` : 'var(--color-surface-sunken)';
  }

  protected pickDay(date: string): void {
    // In der Woche führt der Tageskopf in die Tagesansicht; in Jahr und Monat
    // öffnet sich der Tag als Fenster, weil der Überblick dahinter bleiben soll.
    if (this.view() === 'week') {
      this.anchor.set(date);
      this.view.set('day');
      return;
    }

    this.selectedDay.set(date);
  }

  protected pickEntry(entry: Occurrence): void {
    this.selectedDay.set(entry.localDate);
  }

  protected openDialog(date: string): void {
    this.editing.set(null);
    this.dialogDate.set(date);
  }

  /** Einen Termin aus dem Tagesfenster heraus bearbeiten. */
  protected openEdit(entry: Occurrence): void {
    this.editing.set(entry);
    this.dialogDate.set(entry.localDate);
  }

  protected editable(entry: Occurrence): boolean {
    return this.canPlan() && isEditable(entry);
  }

  protected closeDialog(): void {
    this.dialogDate.set(null);
    this.editing.set(null);
  }

  protected async afterSave(): Promise<void> {
    this.dialogDate.set(null);
    this.editing.set(null);

    // Nur neu laden, kein Fenster aufreißen: Wer aus dem Tagesfenster heraus
    // angelegt hat, steht noch darin und sieht den neuen Termin dort; wer aus
    // der Aktionsleiste kam, sieht ihn im Raster -- wie bei Aufgaben und
    // Kontakten nach dem Speichern.
    const { from, to } = this.range();
    await this.calendar.range(from, to);
  }

  protected timeRange(entry: Occurrence): string {
    if (entry.isAllDay) {
      return 'ganztägig';
    }

    const format = (value: string) =>
      new Date(value).toLocaleTimeString('de-DE', { hour: '2-digit', minute: '2-digit' });

    return `${format(entry.startsAt)} – ${format(entry.endsAt)}`;
  }

  protected dayLabel(date: string): string {
    return new Date(`${date}T12:00:00Z`).toLocaleDateString('de-DE', {
      weekday: 'long',
      day: 'numeric',
      month: 'long',
      year: 'numeric',
    });
  }

  protected nextDay(step: number): void {
    const day = this.selectedDay();
    if (day) {
      this.selectedDay.set(addDays(day, step));
    }
  }
}

function today(): string {
  const now = new Date();
  return isoDate(now.getFullYear(), now.getMonth() + 1, now.getDate());
}

function daysInMonth(year: number, month: number): number {
  return new Date(Date.UTC(year, month, 0)).getUTCDate();
}

function addMonths(year: number, month: number, count: number): { year: number; month: number } {
  const absolute = year * 12 + (month - 1) + count;
  return { year: Math.floor(absolute / 12), month: (absolute % 12) + 1 };
}

/** Die Woche beginnt am Montag -- so zählt der Kulturbetrieb seine Dienste. */
function mondayOf(date: string): string {
  const day = new Date(`${date}T00:00:00Z`).getUTCDay();
  return addDays(date, -((day + 6) % 7));
}

/**
 * Die Kalenderwoche nach ISO 8601: Woche 1 ist die mit dem ersten Donnerstag
 * des Jahres. Der Dienstplan rechnet damit, nicht mit „die wievielte Woche
 * seit Neujahr".
 */
function isoWeek(monday: string): number {
  const thursday = new Date(`${addDays(monday, 3)}T00:00:00Z`);
  const firstOfYear = new Date(Date.UTC(thursday.getUTCFullYear(), 0, 1));
  const days = (thursday.getTime() - firstOfYear.getTime()) / 86400000;
  return Math.floor(days / 7) + 1;
}

function shortDate(date: string): string {
  return new Date(`${date}T12:00:00Z`).toLocaleDateString('de-DE', {
    day: 'numeric',
    month: 'numeric',
  });
}

function longDate(date: string): string {
  return new Date(`${date}T12:00:00Z`).toLocaleDateString('de-DE', {
    day: 'numeric',
    month: 'long',
    year: 'numeric',
  });
}
