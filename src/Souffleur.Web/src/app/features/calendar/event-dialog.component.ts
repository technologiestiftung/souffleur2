import {
  Component,
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
  Conflict,
  EventStatus,
  Frequency,
  Occurrence,
  ParticipantInput,
  ParticipationStatus,
  SeriesInput,
  Weekday,
} from '../../core/calendar.service';
import { CatalogEntry } from '../../core/catalogs.service';
import { Person } from '../../core/people.service';
import { Production } from '../../core/productions.service';
import { Resource } from '../../core/resources.service';
import { DialogComponent } from '../../shared/dialog.component';
import { MeldungComponent } from '../../shared/meldung.component';

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
 * Termin anlegen -- einzeln oder als Serie.
 *
 * Die Wiederholung steht im selben Fenster und nicht hinter einem zweiten
 * Knopf: Ob eine Probe einmal oder jeden Dienstag stattfindet, weiß man beim
 * Eintragen, nicht danach.
 */
@Component({
  selector: 'souffleur-event-dialog',
  imports: [FormsModule, DialogComponent, MeldungComponent],
  templateUrl: './event-dialog.component.html',
})
export class EventDialogComponent {
  private readonly calendar = inject(CalendarService);

  readonly date = input.required<string>();
  readonly categories = input.required<readonly CatalogEntry[]>();
  readonly resources = input.required<readonly Resource[]>();
  readonly people = input.required<readonly Person[]>();
  readonly functions = input.required<readonly CatalogEntry[]>();

  /** Die laufenden Produktionen zur Auswahl; leer, wenn das Modul aus ist. */
  readonly productions = input<readonly Production[]>([]);

  /** Aus dem Produktionsbuch geöffnet: Das Feld steht schon richtig. */
  readonly production = input<string>('');

  readonly saved = output<void>();
  readonly closed = output<void>();

  protected readonly title = model('');
  protected readonly location = model('');
  protected readonly description = model('');
  protected readonly categoryId = model<string>('');
  protected readonly productionId = model<string>('');
  protected readonly status = model(EventStatus.Confirmed);
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

  protected readonly weekdayOptions = WEEKDAY_OPTIONS;
  protected readonly statuses = [
    { value: EventStatus.Confirmed, label: 'Fest' },
    { value: EventStatus.Planned, label: 'Vorgemerkt' },
  ];
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
  private forgetCheck(): void {
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
      this.conflicts.set(
        await this.calendar.conflicts(
          this.repeats()
            ? { resourceIds, contactIds, series: this.seriesInput() }
            : {
                resourceIds,
                contactIds,
                startsAt: this.instant(this.isAllDay() ? '00:00' : this.startTime()),
                endsAt: this.instant(this.isAllDay() ? '23:59' : this.endTime()),
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
   * Anlegen. `force` setzt eine gemeldete Doppelbelegung durch -- der Knopf
   * dafür erscheint erst, wenn der Server eine gemeldet hat.
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
      if (this.repeats()) {
        await this.calendar.createSeries({
          ...this.seriesInput(),
          resourceIds,
          participants,
          force,
        });
      } else {
        await this.calendar.createEvent({
          title: this.title().trim(),
          location: this.location().trim() || null,
          description: this.description().trim() || null,
          categoryId: this.categoryId() || null,
          productionId: this.productionId() || null,
          status: this.status(),
          isAllDay: this.isAllDay(),
          startsAt: this.instant(this.isAllDay() ? '00:00' : this.startTime()),
          endsAt: this.instant(this.isAllDay() ? '23:59' : this.endTime()),
          resourceIds,
          participants,
          force,
        });
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
      status: this.status(),
      isAllDay: this.isAllDay(),
      startDate: this.date(),
      startTime: this.isAllDay() ? '00:00:00' : `${this.startTime()}:00`,
      durationMinutes: this.durationMinutes(),
      frequency: this.frequency(),
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
    const [year, month, day] = this.date().split('-').map(Number);
    const [hour, minute] = time.split(':').map(Number);
    return new Date(year, month - 1, day, hour, minute).toISOString();
  }
}

function toMinutes(time: string): number {
  const [hour, minute] = time.split(':').map(Number);
  return hour * 60 + minute;
}

/**
 * Die Doppelbelegungen aus einer 409-Antwort. Eine Doppelbelegung ist kein
 * Eingabefehler -- sie kommt deshalb nicht als Satz, sondern als Liste.
 */
export function conflictsFrom(error: unknown): Conflict[] {
  const response = error as { status?: number; error?: { conflicts?: Conflict[] } };
  return response?.status === 409 ? (response.error?.conflicts ?? []) : [];
}

/** Die Gründe aus einer ValidationProblem-Antwort des Servers. */
export function messagesFrom(error: unknown): string[] {
  const errors = (error as { error?: { errors?: Record<string, string[]> } })?.error?.errors;
  const found = errors ? Object.values(errors).flat() : [];
  return found.length > 0 ? found : ['Das hat nicht geklappt.'];
}

export type { Occurrence };
