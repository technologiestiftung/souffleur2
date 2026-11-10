import {
  Component,
  OnInit,
  computed,
  effect,
  inject,
  input,
  model,
  output,
  signal,
  untracked,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import {
  CalendarService,
  CalendarSource,
  Conflict,
  EventDetail,
  EventInput,
  EventStatus,
  Frequency,
  Occurrence,
  ParticipantInput,
  ParticipationStatus,
  SeriesDetail,
  SeriesInput,
  Weekday,
} from '../../core/calendar.service';
import { ModulesService } from '../../core/modules.service';
import { CatalogEntry } from '../../core/catalogs.service';
import { Person } from '../../core/people.service';
import { Production } from '../../core/productions.service';
import { Resource } from '../../core/resources.service';
import { DialogComponent } from '../../shared/dialog.component';
import { DokumenteAmZielComponent } from '../../shared/dokumente-am-ziel.component';
import { LesezeichenComponent } from '../../shared/lesezeichen.component';
import { MeldungComponent } from '../../shared/meldung.component';
import { messagesFrom } from '../../shared/fehler';

const WEEKDAY_OPTIONS = [
  { flag: Weekday.Monday, label: 'Mo' },
  { flag: Weekday.Tuesday, label: 'Di' },
  { flag: Weekday.Wednesday, label: 'Mi' },
  { flag: Weekday.Thursday, label: 'Do' },
  { flag: Weekday.Friday, label: 'Fr' },
  { flag: Weekday.Saturday, label: 'Sa' },
  { flag: Weekday.Sunday, label: 'So' },
];

/**
 * Termin anlegen oder bearbeiten -- einzeln oder als Serie.
 *
 * Die Wiederholung steht im selben Fenster und nicht hinter einem zweiten
 * Knopf: Ob eine Probe einmal oder jeden Dienstag stattfindet, weiß man beim
 * Eintragen, nicht danach.
 *
 * Beim Bearbeiten eines Termins aus einer Serie fragt der Bogen, was gemeint
 * ist: nur dieser Abend (eine Abweichung, die eine spätere Regeländerung
 * nicht kassiert) oder die ganze Serie. Ein Termin wird nicht nachträglich
 * zur Serie und eine Serie nicht zum Einzeltermin -- dafür gibt es Löschen
 * und neu Anlegen.
 */
@Component({
  selector: 'souffleur-event-dialog',
  imports: [
    FormsModule,
    DialogComponent,
    MeldungComponent,
    LesezeichenComponent,
    DokumenteAmZielComponent,
  ],
  templateUrl: './event-dialog.component.html',
})
export class EventDialogComponent implements OnInit {
  private readonly calendar = inject(CalendarService);
  private readonly modules = inject(ModulesService);

  readonly date = input.required<string>();
  readonly categories = input.required<readonly CatalogEntry[]>();
  readonly resources = input.required<readonly Resource[]>();
  readonly people = input.required<readonly Person[]>();
  readonly functions = input.required<readonly CatalogEntry[]>();

  /** Die laufenden Produktionen zur Auswahl; leer, wenn das Modul aus ist. */
  readonly productions = input<readonly Production[]>([]);

  /** Aus dem Produktionsbuch geöffnet: Das Feld steht schon richtig. */
  readonly production = input<string>('');

  /** Der Termin, der bearbeitet wird; ohne ihn legt der Bogen an. */
  readonly edit = input<Occurrence | null>(null);

  readonly saved = output<void>();
  readonly closed = output<void>();

  /** Der Tag des Termins. Beim Anlegen der angeklickte, beim Bearbeiten änderbar. */
  protected readonly day = model('');

  /** Bei einem Termin aus einer Serie: nur dieser Abend oder alle. */
  protected readonly scope = signal<'einzeln' | 'serie'>('einzeln');

  protected readonly loading = signal(false);
  private readonly series = signal<SeriesDetail | null>(null);
  private readonly detail = signal<EventDetail | null>(null);

  protected readonly editing = computed(() => this.edit() !== null);
  protected readonly fromSeries = computed(() => !!this.edit()?.seriesId);
  protected readonly wholeSeries = computed(() => this.fromSeries() && this.scope() === 'serie');

  /**
   * Lesezeichen und Dokumente hängen an einem gespeicherten Termin. Eine
   * Folge aus einer Serie hat erst eine Id, wenn sie einmal abgewichen ist.
   */
  protected readonly attachTo = computed(() =>
    this.wholeSeries() ? null : (this.edit()?.eventId ?? null),
  );
  protected readonly documentsOn = computed(() => this.modules.isEnabled('Documents'));

  /** Löschen fragt einmal nach -- ohne Browserdialog. */
  protected readonly confirmDelete = signal(false);

  protected readonly heading = computed(() => {
    if (!this.editing()) {
      return 'Termin am ' + this.day();
    }
    return this.wholeSeries() ? 'Serie bearbeiten' : 'Termin bearbeiten';
  });

  protected readonly title = model('');
  protected readonly location = model('');
  protected readonly description = model('');
  protected readonly categoryId = model<string>('');
  protected readonly productionId = model<string>('');
  protected readonly status = model<number>(EventStatus.Confirmed);
  protected readonly isAllDay = model(false);
  protected readonly startTime = model('19:30');
  protected readonly endTime = model('22:00');

  protected readonly repeats = model(false);
  protected readonly frequency = model<number>(Frequency.Weekly);
  protected readonly interval = model(1);
  protected readonly weekdays = signal(0);
  protected readonly endMode = model<'never' | 'until' | 'count'>('until');
  protected readonly until = model('');
  protected readonly count = model(10);

  protected readonly chosen = signal<ReadonlySet<string>>(new Set());

  /** Wer eingeteilt ist, in der Reihenfolge der Einteilung. */
  protected readonly assigned = signal<ParticipantInput[]>([]);

  protected readonly participationStatuses = [
    { value: ParticipationStatus.Assigned, label: 'Eingeteilt' },
    { value: ParticipationStatus.Accepted, label: 'Zugesagt' },
    { value: ParticipationStatus.Declined, label: 'Abgesagt' },
  ];

  protected readonly saving = signal(false);
  protected readonly checking = signal(false);
  protected readonly problems = signal<string[]>([]);

  /**
   * Was der Bühne im Weg steht. Eine leere Liste heißt nicht „frei" --
   * erst `checked` sagt, dass überhaupt gefragt wurde.
   */
  protected readonly conflicts = signal<Conflict[]>([]);
  protected readonly checked = signal(false);

  constructor() {
    effect(() => {
      const preset = this.production();
      if (preset) {
        untracked(() => this.productionId.set(preset));
      }
    });
  }

  async ngOnInit(): Promise<void> {
    const entry = this.edit();
    if (!entry) {
      this.day.set(this.date());
      return;
    }

    this.loading.set(true);
    try {
      if (entry.seriesId) {
        this.series.set(await this.calendar.getSeries(entry.seriesId));
      }
      if (entry.eventId) {
        this.detail.set(await this.calendar.getEvent(entry.eventId));
      }
      this.fillOccurrence();
    } catch {
      this.problems.set(['Der Termin ließ sich nicht laden.']);
    } finally {
      this.loading.set(false);
    }
  }

  /** Zwischen „nur dieser Termin" und „ganze Serie" wechseln. */
  protected setScope(scope: 'einzeln' | 'serie'): void {
    this.scope.set(scope);
    this.confirmDelete.set(false);
    this.forgetCheck();
    if (scope === 'serie') {
      this.fillSeries();
    } else {
      this.fillOccurrence();
    }
  }

  /**
   * Der Bogen mit dem einen Termin. Eine Abweichung kennt ihre eigenen Räume
   * und Leute; eine Folge, die nie abgewichen ist, hat die der Serie.
   */
  private fillOccurrence(): void {
    const entry = this.edit()!;
    const own = this.detail();
    const series = this.series();

    this.title.set(own?.title ?? entry.title);
    this.location.set(own?.location ?? entry.location ?? '');
    this.description.set(own?.description ?? entry.description ?? '');
    this.categoryId.set(own?.categoryId ?? entry.categoryId ?? '');
    this.productionId.set(own?.productionId ?? series?.productionId ?? '');
    this.status.set(own?.status ?? entry.status);
    this.isAllDay.set(own?.isAllDay ?? entry.isAllDay);

    const starts = new Date(own?.startsAt ?? entry.startsAt);
    const ends = new Date(own?.endsAt ?? entry.endsAt);
    this.day.set(localDate(starts));
    this.startTime.set(localTime(starts));
    this.endTime.set(localTime(ends));

    this.chosen.set(new Set(own?.resourceIds ?? series?.resourceIds ?? []));
    this.assigned.set([...(own?.participants ?? series?.participants ?? [])]);
    this.repeats.set(false);
  }

  /** Der Bogen mit der Regel der Serie -- ab ihrem ersten Termin. */
  private fillSeries(): void {
    const series = this.series();
    if (!series) {
      return;
    }

    this.title.set(series.title);
    this.location.set(series.location ?? '');
    this.description.set(series.description ?? '');
    this.categoryId.set(series.categoryId ?? '');
    this.productionId.set(series.productionId ?? '');
    this.status.set(series.status);
    this.isAllDay.set(series.isAllDay);
    this.day.set(series.startDate);
    this.startTime.set(series.startTime.slice(0, 5));
    this.endTime.set(
      fromMinutes(toMinutes(series.startTime.slice(0, 5)) + series.durationMinutes),
    );

    this.repeats.set(true);
    this.frequency.set(series.frequency);
    this.interval.set(series.interval);
    this.weekdays.set(series.byWeekday);
    this.endMode.set(series.until ? 'until' : series.count ? 'count' : 'never');
    this.until.set(series.until ?? '');
    this.count.set(series.count ?? 10);

    this.chosen.set(new Set(series.resourceIds));
    this.assigned.set([...series.participants]);
  }

  protected readonly weekdayOptions = WEEKDAY_OPTIONS;

  /** Absagen ist ein Stand des Termins -- beim Anlegen ergibt es keinen Sinn. */
  protected readonly statuses = computed(() => [
    { value: EventStatus.Confirmed, label: 'Fest' },
    { value: EventStatus.Planned, label: 'Vorgemerkt' },
    ...(this.editing() ? [{ value: EventStatus.Cancelled, label: 'Abgesagt' }] : []),
  ]);
  protected readonly frequencies = [
    { value: Frequency.Daily, label: 'täglich' },
    { value: Frequency.Weekly, label: 'wöchentlich' },
    { value: Frequency.Monthly, label: 'monatlich' },
    { value: Frequency.Yearly, label: 'jährlich' },
  ];

  protected readonly showWeekdays = computed(() => this.frequency() === Frequency.Weekly);

  protected readonly chosenResources = computed(() =>
    this.resources().filter((resource) => this.chosen().has(resource.id)),
  );

  /** Wer noch nicht dabei ist -- nur die stehen in der Auswahl. */
  protected readonly available = computed(() => {
    const taken = new Set(this.assigned().map((p) => p.contactId));
    return this.people().filter((person) => !taken.has(person.id));
  });

  protected addPerson(id: string): void {
    if (!id) {
      return;
    }

    this.assigned.update((current) => [
      ...current,
      { contactId: id, functionId: null, status: ParticipationStatus.Assigned },
    ]);
    this.forgetCheck();
  }

  protected removePerson(id: string): void {
    this.assigned.update((current) => current.filter((p) => p.contactId !== id));
    this.forgetCheck();
  }

  protected setFunction(id: string, functionId: string): void {
    this.patchPerson(id, { functionId: functionId || null });
  }

  protected setParticipation(id: string, status: number): void {
    // Eine Absage gibt die Person frei -- was im Weg stand, steht es nicht mehr.
    this.patchPerson(id, { status: Number(status) });
    this.forgetCheck();
  }

  protected personName(id: string): string {
    return this.people().find((person) => person.id === id)?.name ?? '?';
  }

  private patchPerson(id: string, change: Partial<ParticipantInput>): void {
    this.assigned.update((current) =>
      current.map((p) => (p.contactId === id ? { ...p, ...change } : p)),
    );
  }

  /** Jede Änderung macht eine frühere Antwort auf „Frei?" ungültig. */
  protected forgetCheck(): void {
    this.conflicts.set([]);
    this.checked.set(false);
  }

  protected toggleResource(id: string): void {
    this.chosen.update((current) => {
      const next = new Set(current);
      if (!next.delete(id)) {
        next.add(id);
      }
      return next;
    });

    this.forgetCheck();
  }

  protected hasResource(id: string): boolean {
    return this.chosen().has(id);
  }

  /** Fragen, ob die gewählten Ressourcen frei sind -- ohne etwas anzulegen. */
  protected async check(): Promise<void> {
    const resourceIds = [...this.chosen()];
    const contactIds = this.occupyingContacts();

    if (resourceIds.length === 0 && contactIds.length === 0) {
      return;
    }

    this.checking.set(true);
    try {
      // Beim Bearbeiten steht der Termin sich nicht selbst im Weg.
      const ignore = {
        ignoreEventId: this.edit()?.eventId ?? undefined,
        ignoreSeriesId: this.edit()?.seriesId ?? undefined,
      };
      this.conflicts.set(
        await this.calendar.conflicts(
          this.repeats()
            ? { resourceIds, contactIds, series: this.seriesInput(), ...ignore }
            : {
                resourceIds,
                contactIds,
                startsAt: this.instant(this.isAllDay() ? '00:00' : this.startTime()),
                endsAt: this.instant(this.isAllDay() ? '23:59' : this.endTime()),
                ...ignore,
              },
        ),
      );
      this.checked.set(true);
    } catch {
      // Keine Antwort ist keine Freigabe -- der Server prüft beim Anlegen
      // noch einmal.
      this.conflicts.set([]);
      this.checked.set(false);
    } finally {
      this.checking.set(false);
    }
  }

  protected conflictTime(conflict: Conflict): string {
    const day = new Date(conflict.startsAt).toLocaleDateString('de-DE', {
      day: 'numeric',
      month: 'numeric',
    });
    const time = new Date(conflict.startsAt).toLocaleTimeString('de-DE', {
      hour: '2-digit',
      minute: '2-digit',
    });
    return `${day}, ${time}`;
  }

  protected toggleWeekday(flag: number): void {
    this.weekdays.update((current) => current ^ flag);
  }

  protected hasWeekday(flag: number): boolean {
    return (this.weekdays() & flag) !== 0;
  }

  /**
   * Anlegen oder speichern. `force` setzt eine gemeldete Doppelbelegung
   * durch -- der Knopf dafür erscheint erst, wenn der Server eine gemeldet hat.
   */
  protected async save(force = false): Promise<void> {
    this.problems.set([]);

    if (!this.title().trim()) {
      this.problems.set(['Ein Titel ist nötig.']);
      return;
    }

    this.saving.set(true);
    const resourceIds = [...this.chosen()];
    const participants = this.assigned();

    try {
      const entry = this.edit();
      const series = { ...this.seriesInput(), resourceIds, participants, force };
      const event: EventInput = { ...this.eventInput(), resourceIds, participants, force };

      if (!entry) {
        await (this.repeats()
          ? this.calendar.createSeries(series)
          : this.calendar.createEvent(event));
      } else if (this.wholeSeries()) {
        await this.calendar.updateSeries(entry.seriesId!, series);
      } else if (entry.seriesId && entry.occurrenceStart) {
        await this.calendar.overrideOccurrence(entry.seriesId, entry.occurrenceStart, event);
      } else {
        await this.calendar.updateEvent(entry.eventId!, event);
      }

      this.saved.emit();
    } catch (error) {
      const occupied = conflictsFrom(error);
      if (occupied.length > 0) {
        // Nicht abgelehnt, sondern angehalten: Der Server hat 409 geantwortet,
        // und der Knopf „Trotzdem anlegen" steht jetzt da.
        this.conflicts.set(occupied);
        this.checked.set(true);
      } else {
        this.problems.set(messagesFrom(error));
      }
    } finally {
      this.saving.set(false);
    }
  }

  /**
   * Löschen: einen Termin oder eine ganze Serie. Ein einzelner Abend einer
   * Serie wird nicht gelöscht, sondern abgesagt -- über den Status.
   */
  protected async remove(): Promise<void> {
    const entry = this.edit();
    if (!entry) {
      return;
    }
    if (!this.confirmDelete()) {
      this.confirmDelete.set(true);
      return;
    }

    this.saving.set(true);
    this.problems.set([]);
    try {
      await (this.wholeSeries()
        ? this.calendar.deleteSeries(entry.seriesId!)
        : this.calendar.deleteEvent(entry.eventId!));
      this.saved.emit();
    } catch (error) {
      this.problems.set(messagesFrom(error));
    } finally {
      this.saving.set(false);
      this.confirmDelete.set(false);
    }
  }

  /** Löschen gibt es für einen Einzeltermin und für die ganze Serie. */
  protected readonly canDelete = computed(
    () => this.editing() && (this.wholeSeries() || !this.fromSeries()),
  );

  private eventInput(): EventInput {
    return {
      title: this.title().trim(),
      location: this.location().trim() || null,
      description: this.description().trim() || null,
      categoryId: this.categoryId() || null,
      productionId: this.productionId() || null,
      status: Number(this.status()),
      isAllDay: this.isAllDay(),
      startsAt: this.instant(this.isAllDay() ? '00:00' : this.startTime()),
      endsAt: this.instant(this.isAllDay() ? '23:59' : this.endTime()),
    };
  }

  /**
   * Wen die Einteilung belegt. Wer abgesagt hat, steht niemandem im Weg --
   * sonst meldete die Prüfung einen Konflikt mit jemandem, der gar nicht
   * kommt.
   */
  private occupyingContacts(): string[] {
    return this.assigned()
      .filter((p) => p.status !== ParticipationStatus.Declined)
      .map((p) => p.contactId);
  }

  private seriesInput(): SeriesInput {
    return {
      title: this.title().trim() || 'Serie',
      location: this.location().trim() || null,
      description: this.description().trim() || null,
      categoryId: this.categoryId() || null,
      productionId: this.productionId() || null,
      status: Number(this.status()),
      isAllDay: this.isAllDay(),
      startDate: this.day(),
      startTime: this.isAllDay() ? '00:00:00' : `${this.startTime()}:00`,
      durationMinutes: this.durationMinutes(),
      frequency: Number(this.frequency()),
      interval: Math.max(1, this.interval()),
      byWeekday: this.showWeekdays() ? this.weekdays() : 0,
      until: this.endMode() === 'until' && this.until() ? this.until() : null,
      count: this.endMode() === 'count' ? this.count() : null,
    };
  }

  private durationMinutes(): number {
    if (this.isAllDay()) {
      return 24 * 60 - 1;
    }

    const minutes = toMinutes(this.endTime()) - toMinutes(this.startTime());

    // Über Mitternacht hinaus: Die Spätvorstellung endet am Folgetag.
    return minutes > 0 ? minutes : minutes + 24 * 60;
  }

  /**
   * Datum und Uhrzeit in einen Zeitpunkt. Gilt in der Zeitzone des Browsers --
   * für ein Haus, dessen Leute vor Ort sitzen, ist das die Zeitzone des Hauses.
   */
  private instant(time: string): string {
    const [year, month, day] = this.day().split('-').map(Number);
    const [hour, minute] = time.split(':').map(Number);
    return new Date(year, month - 1, day, hour, minute).toISOString();
  }
}

function toMinutes(time: string): number {
  const [hour, minute] = time.split(':').map(Number);
  return hour * 60 + minute;
}

/** Minuten seit Mitternacht als Uhrzeit, über Mitternacht hinaus wieder von vorn. */
function fromMinutes(minutes: number): string {
  const wrapped = ((minutes % (24 * 60)) + 24 * 60) % (24 * 60);
  return `${String(Math.floor(wrapped / 60)).padStart(2, '0')}:${String(wrapped % 60).padStart(2, '0')}`;
}

/** Der Kalendertag eines Zeitpunkts im Browser -- wie `instant` rückwärts. */
function localDate(value: Date): string {
  return `${value.getFullYear()}-${String(value.getMonth() + 1).padStart(2, '0')}-${String(value.getDate()).padStart(2, '0')}`;
}

function localTime(value: Date): string {
  return `${String(value.getHours()).padStart(2, '0')}:${String(value.getMinutes()).padStart(2, '0')}`;
}

/** Nur was aus dem Kalender selbst kommt, lässt sich hier bearbeiten. */
export function isEditable(entry: Occurrence): boolean {
  return entry.source === CalendarSource.Calendar && (!!entry.eventId || !!entry.seriesId);
}

/**
 * Die Doppelbelegungen aus einer 409-Antwort. Eine Doppelbelegung ist kein
 * Eingabefehler -- sie kommt deshalb nicht als Satz, sondern als Liste.
 */
export function conflictsFrom(error: unknown): Conflict[] {
  const response = error as { status?: number; error?: { conflicts?: Conflict[] } };
  return response?.status === 409 ? (response.error?.conflicts ?? []) : [];
}

export { messagesFrom };

export type { Occurrence };
