import { Component, computed, effect, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { addDays, isoDate } from '../../core/calendar.service';
import {
  Breach,
  Timeline,
  TimelineEntry,
  TasksService,
} from '../../core/tasks.service';

/** Höhe einer Zeile in Pixeln. Fest, damit die Pfeile rechnen können. */
const ROW = 30;

/** Wie breit der Zeitbereich werden darf, bevor er scrollt. */
const WIDTH = 1100;

const MONTHS = [
  'Jan', 'Feb', 'Mär', 'Apr', 'Mai', 'Jun',
  'Jul', 'Aug', 'Sep', 'Okt', 'Nov', 'Dez',
];

/**
 * Der Zeitstrahl der Aufgaben -- siehe ADR-0007.
 *
 * Eine andere Frage als das Jahresraster des Kalenders: Dort geht es darum,
 * was an einem Tag los ist, hier darum, wie lange etwas dauert und was daran
 * hängt. Deshalb Vorgänge untereinander und Zeit nach rechts.
 */
@Component({
  selector: 'souffleur-task-timeline',
  imports: [FormsModule],
  templateUrl: './timeline.component.html',
  host: { class: 'block h-full min-h-0' },
})
export class TaskTimelineComponent {
  private readonly tasks = inject(TasksService);

  readonly includeClosed = input(false);

  protected readonly spans = [3, 6, 12];
  protected readonly monthCount = signal(3);
  protected readonly anchor = signal(firstOfThisMonth());

  protected readonly timeline = signal<Timeline | null>(null);
  protected readonly loading = signal(false);

  protected readonly rowHeight = ROW;

  protected readonly range = computed(() => {
    const [year, month] = this.anchor().split('-').map(Number);
    const last = addMonths(year, month, this.monthCount() - 1);
    return {
      from: isoDate(year, month, 1),
      to: isoDate(last.year, last.month, daysInMonth(last.year, last.month)),
    };
  });

  /** Wie viele Pixel ein Tag bekommt. Eng genug, dass das Jahr noch passt. */
  protected readonly dayWidth = computed(() =>
    Math.max(3, Math.round(WIDTH / Math.max(1, this.days()))),
  );

  protected readonly days = computed(() => {
    const { from, to } = this.range();
    return Math.round((Date.parse(`${to}T00:00:00Z`) - Date.parse(`${from}T00:00:00Z`))
      / 86_400_000) + 1;
  });

  protected readonly width = computed(() => this.days() * this.dayWidth());

  protected readonly entries = computed(() => this.timeline()?.tasks ?? []);

  protected readonly height = computed(() => Math.max(1, this.entries().length) * ROW);

  /** Die Monatsköpfe über dem Band. */
  protected readonly months = computed(() => {
    const { from } = this.range();
    const [startYear, startMonth] = from.split('-').map(Number);

    return Array.from({ length: this.monthCount() }, (_, index) => {
      const { year, month } = addMonths(startYear, startMonth, index);
      const first = isoDate(year, month, 1);
      return {
        label: `${MONTHS[month - 1]} ${year}`,
        left: this.offset(first) * this.dayWidth(),
        width: daysInMonth(year, month) * this.dayWidth(),
      };
    });
  });

  /** Die rote Linie für heute -- nur wenn sie in den Ausschnitt fällt. */
  protected readonly todayLeft = computed(() => {
    const offset = this.offset(today());
    return offset >= 0 && offset < this.days() ? offset * this.dayWidth() : null;
  });

  protected readonly breaches = computed(() => this.timeline()?.breaches ?? []);

  /**
   * Die Pfeile: vom Ende dessen, worauf gewartet wird, zum Anfang dessen,
   * was wartet. Als Winkel gezeichnet, nicht als Gerade -- eine Diagonale
   * über zehn Zeilen lässt sich nicht verfolgen.
   */
  protected readonly arrows = computed(() => {
    const data = this.timeline();
    if (!data) {
      return [];
    }

    const rows = new Map(this.entries().map((task, index) => [task.id, index]));
    const broken = new Set(this.breaches().map((b) => `${b.taskId}:${b.dependsOnId}`));

    return data.dependencies.flatMap((link) => {
      const waiting = this.entries().find((t) => t.id === link.taskId);
      const earlier = this.entries().find((t) => t.id === link.dependsOnId);
      const fromRow = rows.get(link.dependsOnId);
      const toRow = rows.get(link.taskId);

      if (!waiting || !earlier || fromRow === undefined || toRow === undefined) {
        return [];
      }

      const x1 = this.right(earlier);
      const y1 = fromRow * ROW + ROW / 2;
      const x2 = this.left(waiting);
      const y2 = toRow * ROW + ROW / 2;

      // Ein Stück heraus, dann senkrecht, dann hinein.
      const turn = Math.max(x1 + 8, x2 - 8);

      return [{
        points: `${x1},${y1} ${turn},${y1} ${turn},${y2} ${x2},${y2}`,
        tip: `${x2},${y2} ${x2 - 5},${y2 - 3.5} ${x2 - 5},${y2 + 3.5}`,
        broken: broken.has(`${link.taskId}:${link.dependsOnId}`),
        title: `„${waiting.title}" wartet auf „${earlier.title}"`,
      }];
    });
  });

  constructor() {
    effect(() => {
      const { from, to } = this.range();
      const closed = this.includeClosed();
      void this.load(from, to, closed);
    });
  }

  protected left(task: TimelineEntry): number {
    return this.offset(task.from) * this.dayWidth();
  }

  protected barWidth(task: TimelineEntry): number {
    const span = this.offset(task.to) - this.offset(task.from) + 1;
    return Math.max(this.dayWidth(), span * this.dayWidth());
  }

  protected right(task: TimelineEntry): number {
    return this.left(task) + this.barWidth(task);
  }

  protected shift(direction: number): void {
    const [year, month] = this.anchor().split('-').map(Number);
    const next = addMonths(year, month, direction * this.monthCount());
    this.anchor.set(isoDate(next.year, next.month, 1));
  }

  protected goToday(): void {
    this.anchor.set(firstOfThisMonth());
  }

  protected label(): string {
    const { from, to } = this.range();
    const [fromYear, fromMonth] = from.split('-').map(Number);
    const [toYear, toMonth] = to.split('-').map(Number);
    return `${MONTHS[fromMonth - 1]} ${fromYear} – ${MONTHS[toMonth - 1]} ${toYear}`;
  }

  protected isBroken(task: TimelineEntry): boolean {
    return this.breaches().some((b) => b.taskId === task.id);
  }

  /** Die Kategoriefarbe, stark verdünnt -- der Balken trägt Text. */
  protected wash(task: TimelineEntry): string {
    return task.categoryColor
      ? `color-mix(in oklab, ${task.categoryColor} 20%, transparent)`
      : 'var(--color-surface-sunken)';
  }

  protected accent(task: TimelineEntry): string {
    return task.categoryColor ?? 'var(--color-border-strong)';
  }

  protected reason(breach: Breach): string {
    return `„${breach.taskTitle}" beginnt, bevor „${breach.dependsOnTitle}" fertig ist.`;
  }

  /** Tage zwischen dem Anfang des Ausschnitts und einem Datum. */
  private offset(date: string): number {
    const start = Date.parse(`${this.range().from}T00:00:00Z`);
    return Math.round((Date.parse(`${date}T00:00:00Z`) - start) / 86_400_000);
  }

  private async load(from: string, to: string, includeClosed: boolean): Promise<void> {
    this.loading.set(true);
    try {
      this.timeline.set(await this.tasks.timeline(from, to, includeClosed));
    } catch {
      this.timeline.set(null);
    } finally {
      this.loading.set(false);
    }
  }
}

function today(): string {
  const now = new Date();
  return isoDate(now.getFullYear(), now.getMonth() + 1, now.getDate());
}

function firstOfThisMonth(): string {
  const now = new Date();
  return isoDate(now.getFullYear(), now.getMonth() + 1, 1);
}

function daysInMonth(year: number, month: number): number {
  return new Date(Date.UTC(year, month, 0)).getUTCDate();
}

function addMonths(year: number, month: number, count: number): { year: number; month: number } {
  const absolute = year * 12 + (month - 1) + count;
  return { year: Math.floor(absolute / 12), month: (absolute % 12) + 1 };
}

export { addDays };
