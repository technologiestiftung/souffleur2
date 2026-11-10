import { DatePipe } from '@angular/common';
import { Component, OnDestroy, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { CatalogEntry, CatalogsService } from '../../core/catalogs.service';
import { MyClock, TimeEntry, TimeInput, TimesService, asHours } from '../../core/times.service';
import { DialogComponent } from '../../shared/dialog.component';
import { messagesFrom } from '../calendar/event-dialog.component';
import { MeldungComponent } from '../../shared/meldung.component';

/** Ein Tag mit seinen Zeiten und ihrer Summe. */
interface TimeDay {
  readonly date: string;
  readonly entries: readonly TimeEntry[];
  readonly minutes: number;
}

/**
 * Die eigenen Zeiten: kommen, gehen, nachtragen, richtigstellen.
 *
 * Eine Woche auf einmal, weil die Frage „komme ich hin?" wöchentlich gestellt
 * wird und nicht täglich. Die Tagesgrenzen zieht der Browser -- für ein Haus,
 * dessen Leute vor Ort sitzen, ist das die Zeitzone des Hauses.
 */
@Component({
  selector: 'souffleur-zeiten',
  imports: [DatePipe, FormsModule, DialogComponent, MeldungComponent],
  templateUrl: './zeiten.component.html',
})
export class ZeitenComponent implements OnInit, OnDestroy {
  private readonly times = inject(TimesService);
  private readonly catalogs = inject(CatalogsService);

  protected readonly clock = signal<MyClock | null>(null);
  protected readonly entries = signal<TimeEntry[]>([]);
  protected readonly functions = signal<CatalogEntry[]>([]);

  protected readonly loading = signal(true);
  protected readonly busy = signal(false);
  protected readonly problems = signal<string[]>([]);

  /** Der Montag der gezeigten Woche. */
  protected readonly monday = signal(mondayOf(new Date()));

  /** Was beim Kommen und Gehen mitgegeben wird. */
  protected readonly functionId = signal<string>('');
  protected readonly breakMinutes = signal(30);

  /** Der offene Bogen: 'neu' für einen Nachtrag, sonst die Kennung. */
  protected readonly editing = signal<string | null>(null);
  protected readonly draft = signal<Draft>(emptyDraft());
  protected readonly reason = signal('');

  /** Tickt, damit die laufende Zeit nicht stehenbleibt. */
  private readonly tick = signal(0);
  private timer?: ReturnType<typeof setInterval>;

  protected readonly asHours = asHours;

  protected readonly sunday = computed(() => {
    const end = new Date(this.monday());
    end.setDate(end.getDate() + 6);
    return end;
  });

  protected readonly days = computed<TimeDay[]>(() => {
    const byDate = new Map<string, TimeEntry[]>();

    for (const entry of this.entries()) {
      const key = localDate(new Date(entry.startedAt));
      const list = byDate.get(key);
      if (list) {
        list.push(entry);
      } else {
        byDate.set(key, [entry]);
      }
    }

    return [...byDate.entries()]
      .sort((a, b) => a[0].localeCompare(b[0]))
      .map(([date, list]) => ({
        date,
        entries: list,
        minutes: list.reduce((sum, entry) => sum + (entry.workedMinutes ?? 0), 0),
      }));
  });

  protected readonly weekMinutes = computed(() =>
    this.days().reduce((sum, day) => sum + day.minutes, 0),
  );

  /** Wie lange die laufende Zeit schon läuft. */
  protected readonly runningMinutes = computed(() => {
    this.tick();
    const running = this.clock()?.running;

    return running
      ? Math.max(0, Math.floor((Date.now() - new Date(running.startedAt).getTime()) / 60000))
      : 0;
  });

  async ngOnInit(): Promise<void> {
    this.timer = setInterval(() => this.tick.update((value) => value + 1), 30_000);

    try {
      this.functions.set(await this.catalogs.entries('ParticipationRole'));
    } catch {
      // Ohne Funktionen geht es auch -- dann steht die Auswahl eben leer.
      this.functions.set([]);
    }

    await this.reload();
    this.loading.set(false);
  }

  ngOnDestroy(): void {
    clearInterval(this.timer);
  }

  protected async reload(): Promise<void> {
    this.problems.set([]);

    try {
      this.clock.set(await this.times.me());

      if (this.clock()?.contactId) {
        const end = new Date(this.sunday());
        end.setHours(23, 59, 59);
        this.entries.set(await this.times.list(this.monday(), end));
      }
    } catch (error) {
      this.problems.set(messagesFrom(error));
    }
  }

  protected async shiftWeek(weeks: number): Promise<void> {
    const next = new Date(this.monday());
    next.setDate(next.getDate() + weeks * 7);
    this.monday.set(next);
    await this.reload();
  }

  protected async thisWeek(): Promise<void> {
    this.monday.set(mondayOf(new Date()));
    await this.reload();
  }

  protected async clockIn(): Promise<void> {
    await this.run(() => this.times.clockIn(this.functionId() || null));
  }

  protected async clockOut(): Promise<void> {
    await this.run(() => this.times.clockOut(this.breakMinutes()));
  }

  protected async discard(): Promise<void> {
    await this.run(() => this.times.discard());
  }

  protected startNew(): void {
    this.problems.set([]);
    this.reason.set('');
    this.editing.set('neu');

    const day = new Date();
    this.draft.set({
      date: localDate(day),
      from: '09:00',
      to: '17:00',
      breakMinutes: 30,
      functionId: '',
      note: '',
    });
  }

  protected startEdit(entry: TimeEntry): void {
    this.problems.set([]);
    this.reason.set('');
    this.editing.set(entry.id);

    const started = new Date(entry.startedAt);
    const ended = entry.endedAt ? new Date(entry.endedAt) : started;

    this.draft.set({
      date: localDate(started),
      from: clockTime(started),
      to: clockTime(ended),
      breakMinutes: entry.breakMinutes,
      functionId: entry.functionId ?? '',
      note: entry.note ?? '',
    });
  }

  protected cancel(): void {
    this.editing.set(null);
    this.draft.set(emptyDraft());
    this.reason.set('');
  }

  protected patch(change: Partial<Draft>): void {
    this.draft.update((current) => ({ ...current, ...change }));
  }

  protected async save(): Promise<void> {
    const draft = this.draft();
    const input: TimeInput = {
      startedAt: instant(draft.date, draft.from),
      endedAt: instant(draft.date, draft.to),
      breakMinutes: Number(draft.breakMinutes) || 0,
      functionId: draft.functionId || null,
      note: draft.note.trim() || null,
    };

    const id = this.editing();

    await this.run(async () => {
      if (id === 'neu') {
        await this.times.add(input);
      } else if (id) {
        await this.times.correct(id, input, this.reason());
      }

      this.cancel();
    });
  }

  /** Ausführen, Fehler zeigen, danach neu laden. */
  private async run(action: () => Promise<unknown>): Promise<void> {
    this.busy.set(true);
    this.problems.set([]);

    try {
      await action();
      await this.reload();
    } catch (error) {
      this.problems.set(messagesFrom(error));
    } finally {
      this.busy.set(false);
    }
  }
}

interface Draft {
  date: string;
  from: string;
  to: string;
  breakMinutes: number;
  functionId: string;
  note: string;
}

function emptyDraft(): Draft {
  return { date: '', from: '', to: '', breakMinutes: 0, functionId: '', note: '' };
}

/** Ortszeit, nicht UTC: toISOString() verschiebt den Tag. */
function localDate(date: Date): string {
  const month = String(date.getMonth() + 1).padStart(2, '0');
  const day = String(date.getDate()).padStart(2, '0');
  return `${date.getFullYear()}-${month}-${day}`;
}

function clockTime(date: Date): string {
  return `${String(date.getHours()).padStart(2, '0')}:${String(date.getMinutes()).padStart(2, '0')}`;
}

/** Tag und Uhrzeit in einen Zeitpunkt, in der Zeitzone des Browsers. */
function instant(date: string, time: string): string {
  const [year, month, day] = date.split('-').map(Number);
  const [hour, minute] = time.split(':').map(Number);
  return new Date(year, month - 1, day, hour, minute).toISOString();
}

/** Die Woche beginnt am Montag -- auch wenn der Sonntag davorliegt. */
function mondayOf(date: Date): Date {
  const monday = new Date(date);
  const weekday = (monday.getDay() + 6) % 7;
  monday.setDate(monday.getDate() - weekday);
  monday.setHours(0, 0, 0, 0);
  return monday;
}
