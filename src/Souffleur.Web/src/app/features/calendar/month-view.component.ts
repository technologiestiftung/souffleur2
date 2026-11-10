import { Component, computed, input, output } from '@angular/core';
import { Occurrence, addDays, isoDate } from '../../core/calendar.service';

interface Day {
  readonly date: string;
  readonly number: number;
  readonly inMonth: boolean;
  readonly isWeekend: boolean;
  readonly isToday: boolean;
  readonly entries: readonly Occurrence[];
}

const WEEKDAYS = ['Montag', 'Dienstag', 'Mittwoch', 'Donnerstag', 'Freitag', 'Samstag', 'Sonntag'];

/**
 * Der Monat im gewohnten Raster: sieben Spalten, Wochen als Zeilen.
 *
 * Neben dem Jahresraster, nicht statt seiner (ADR-0006). Das Jahr beantwortet
 * „wie verteilt sich die Spielzeit", der Monat „was steht die nächsten Wochen
 * an" -- und dafür ist die Woche als Zeile die richtige Einheit.
 */
@Component({
  selector: 'souffleur-month-view',
  templateUrl: './month-view.component.html',
  host: { class: 'block h-full min-h-0' },
})
export class MonthViewComponent {
  readonly year = input.required<number>();
  readonly month = input.required<number>();
  readonly byDate = input.required<Map<string, Occurrence[]>>();

  readonly dayPicked = output<string>();
  readonly entryPicked = output<Occurrence>();

  protected readonly weekdays = WEEKDAYS;

  private readonly today = isoDate(
    new Date().getFullYear(),
    new Date().getMonth() + 1,
    new Date().getDate(),
  );

  /**
   * Immer ganze Wochen, auch wenn der Monat mitten in einer anfängt: Sonst
   * stünde die erste Zeile eingerückt und die Spalten hießen nicht mehr, was
   * oben steht.
   */
  protected readonly weeks = computed<Day[][]>(() => {
    const entries = this.byDate();
    const first = isoDate(this.year(), this.month(), 1);
    const start = mondayOf(first);

    const weeks: Day[][] = [];
    let cursor = start;

    // Sechs Wochen decken jeden Monat ab; die letzte entfällt, wenn sie
    // vollständig im Folgemonat liegt.
    for (let week = 0; week < 6; week++) {
      const days: Day[] = [];

      for (let index = 0; index < 7; index++) {
        const parts = cursor.split('-').map(Number);
        days.push({
          date: cursor,
          number: parts[2],
          inMonth: parts[0] === this.year() && parts[1] === this.month(),
          isWeekend: index >= 5,
          isToday: cursor === this.today,
          entries: entries.get(cursor) ?? [],
        });
        cursor = addDays(cursor, 1);
      }

      if (week === 5 && !days.some((d) => d.inMonth)) {
        break;
      }

      weeks.push(days);
    }

    return weeks;
  });

  protected shown(day: Day): readonly Occurrence[] {
    return day.entries.length > 5 ? day.entries.slice(0, 4) : day.entries;
  }

  protected hidden(day: Day): number {
    return Math.max(0, day.entries.length - this.shown(day).length);
  }

  protected wash(color: string | null): string {
    return color
      ? `color-mix(in oklab, ${color} 16%, transparent)`
      : 'var(--color-surface-sunken)';
  }

  protected accent(color: string | null): string {
    return color ?? 'var(--color-border-strong)';
  }

  protected time(entry: Occurrence): string {
    return entry.isAllDay
      ? ''
      : new Date(entry.startsAt).toLocaleTimeString('de-DE', {
          hour: '2-digit',
          minute: '2-digit',
        });
  }

  protected tooltip(entry: Occurrence): string {
    const parts = [entry.title];
    if (!entry.isAllDay) {
      parts.push(this.time(entry));
    }
    if (entry.location) {
      parts.push(entry.location);
    }
    return parts.join(' · ');
  }
}

/** Die Woche beginnt am Montag -- so zählt der Kulturbetrieb seine Dienste. */
function mondayOf(date: string): string {
  const day = new Date(`${date}T00:00:00Z`).getUTCDay();
  return addDays(date, -((day + 6) % 7));
}
