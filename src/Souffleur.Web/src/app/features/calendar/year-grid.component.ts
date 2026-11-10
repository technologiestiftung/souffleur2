import { Component, computed, input, output } from '@angular/core';
import { Occurrence, isoDate } from '../../core/calendar.service';

interface Cell {
  readonly date: string | null;
  readonly weekday: string;
  readonly isWeekend: boolean;
  readonly isToday: boolean;
  readonly entries: readonly Occurrence[];
}

interface Row {
  readonly day: number;
  readonly cells: readonly Cell[];
}

interface MonthHead {
  readonly label: string;
  readonly year: number;
  readonly isYearStart: boolean;
}

const WEEKDAYS = ['So', 'Mo', 'Di', 'Mi', 'Do', 'Fr', 'Sa'];

const MONTHS = [
  'Januar', 'Februar', 'März', 'April', 'Mai', 'Juni',
  'Juli', 'August', 'September', 'Oktober', 'November', 'Dezember',
];

/**
 * Die Jahresansicht: Monate als Spalten, Tage als Zeilen.
 *
 * Das ist die Leitansicht des Kalenders, nicht der Monat (ADR-0006). Die Fragen
 * eines Theaters -- wann ist probenfreie Zeit, wie verteilen sich die
 * Vorstellungen über die Spielzeit, kollidiert die Wiederaufnahme mit dem
 * Gastspiel -- passen in keinen Monat.
 *
 * Deshalb steht in jeder Zelle auch, *was* los ist, und nicht nur ein Punkt.
 * Ein Raster, das nur Belegung anzeigt, muss man für jede Frage verlassen.
 */
@Component({
  selector: 'souffleur-year-grid',
  templateUrl: './year-grid.component.html',
  host: { class: 'block h-full min-h-0' },
})
export class YearGridComponent {
  readonly startYear = input.required<number>();
  readonly startMonth = input.required<number>();
  readonly monthCount = input.required<number>();
  readonly byDate = input.required<Map<string, Occurrence[]>>();

  readonly dayPicked = output<string>();
  readonly entryPicked = output<Occurrence>();

  /** Wie viele Termine je Zelle stehen, bevor „+n" übernimmt. */
  private readonly visiblePerDay = 4;

  /**
   * Ab wie vielen Tagen ein Termin als Dauerlauf gilt und nicht mehr in jedem
   * Tag erscheint, über den er läuft.
   *
   * Ein Gastspiel über eine Woche gehört an jeden seiner Tage -- man plant
   * daran. Eine Installation über fünf Jahre nicht: Sie stünde in jeder Zelle
   * des Rasters und verdeckte genau das, wofür die Ansicht da ist. Bei
   * einem Haus waren das achtzehn Monatsspalten, in denen vor lauter
   * Dauerterminen keine Probe mehr zu sehen war.
   */
  private readonly longRunDays = 8;

  private readonly today = isoDate(
    new Date().getFullYear(),
    new Date().getMonth() + 1,
    new Date().getDate(),
  );

  protected readonly months = computed<MonthHead[]>(() => {
    const result: MonthHead[] = [];

    for (let index = 0; index < this.monthCount(); index++) {
      const { year, month } = this.monthAt(index);
      result.push({
        // Bei vielen Spalten wird der Name gekürzt -- sonst steht er nicht mehr
        // über seiner Spalte, sondern quer durch die Nachbarn.
        label: this.monthCount() > 12 ? MONTHS[month - 1].slice(0, 3) : MONTHS[month - 1],
        year,
        isYearStart: month === 1 || index === 0,
      });
    }

    return result;
  });

  protected readonly rows = computed<Row[]>(() => {
    const entries = this.byDate();
    const count = this.monthCount();
    const result: Row[] = [];

    for (let day = 1; day <= 31; day++) {
      const cells: Cell[] = [];

      for (let index = 0; index < count; index++) {
        const { year, month } = this.monthAt(index);

        // Den 31. gibt es nicht in jedem Monat. Die Zelle bleibt leer, statt
        // dass die Spalten gegeneinander verrutschen.
        if (day > daysInMonth(year, month)) {
          cells.push({ date: null, weekday: '', isWeekend: false, isToday: false, entries: [] });
          continue;
        }

        const date = isoDate(year, month, day);
        const weekday = new Date(`${date}T00:00:00Z`).getUTCDay();

        cells.push({
          date,
          weekday: WEEKDAYS[weekday],
          isWeekend: weekday === 0 || weekday === 6,
          isToday: date === this.today,
          entries: (entries.get(date) ?? []).filter(
            (entry) => this.belongsHere(entry, date, day),
          ),
        });
      }

      result.push({ day, cells });
    }

    return result;
  });

  /**
   * Die Spaltenbreite. 6,5rem als Mindestmaß, damit zwölf Monate auf einen
   * üblichen Bildschirm passen, ohne zu scrollen -- das ist der häufigste
   * Ausschnitt. Darüber hinaus wird gescrollt, und das ist richtig so: Eine
   * Jahresdisposition auf achtzehn Monate ist breiter als jeder Bildschirm.
   */
  protected readonly columns = computed(
    () => `2.5rem repeat(${this.monthCount()}, minmax(6.5rem, 1fr))`,
  );

  /**
   * Ob ein Termin in dieser Zelle auftaucht. Kurze Termine überall, wo sie
   * laufen; ein Dauerlauf nur an seinem ersten Tag und danach einmal je
   * Monatsspalte, damit er nicht aus dem Blick gerät, aber auch keinen
   * Monat zupflastert.
   */
  private belongsHere(entry: Occurrence, date: string, day: number): boolean {
    if (entry.spanDays < this.longRunDays) {
      return true;
    }

    return date === entry.localDate || day === 1;
  }

  /** Ein Dauerlauf: läuft über diesen Tag hinaus weiter. */
  protected runsOn(entry: Occurrence): boolean {
    return entry.spanDays >= this.longRunDays;
  }

  /** „bis 22.04.2032" -- sonst sieht ein Dauerlauf aus wie ein Tagestermin. */
  protected until(entry: Occurrence): string {
    return new Date(`${entry.localEndDate}T00:00:00Z`).toLocaleDateString('de-DE', {
      day: '2-digit',
      month: '2-digit',
      year: 'numeric',
      timeZone: 'UTC',
    });
  }

  protected shown(cell: Cell): readonly Occurrence[] {
    return cell.entries.length > this.visiblePerDay
      ? cell.entries.slice(0, this.visiblePerDay - 1)
      : cell.entries;
  }

  protected hidden(cell: Cell): number {
    return Math.max(0, cell.entries.length - this.shown(cell).length);
  }

  /**
   * Die Kategoriefarbe als Hintergrund, stark verdünnt. Gemischt statt mit
   * Deckkraft gesetzt, damit die Schrift darüber voll deckend bleibt -- und in
   * oklab, damit die Mischung in hell wie dunkel gleich hell wirkt.
   */
  protected wash(color: string | null): string {
    return color
      ? `color-mix(in oklab, ${color} 16%, transparent)`
      : 'var(--color-surface-sunken)';
  }

  protected accent(color: string | null): string {
    return color ?? 'var(--color-border-strong)';
  }

  /** Nur die Uhrzeit -- der Tag steht schon in der Zeile. */
  protected time(entry: Occurrence): string {
    if (entry.isAllDay) {
      return '';
    }

    return new Date(entry.startsAt).toLocaleTimeString('de-DE', {
      hour: '2-digit',
      minute: '2-digit',
    });
  }

  protected tooltip(entry: Occurrence): string {
    const parts = [entry.title];

    if (!entry.isAllDay) {
      parts.push(this.time(entry));
    }

    if (this.runsOn(entry)) {
      parts.push(`läuft bis ${this.until(entry)}`);
    }

    if (entry.location) {
      parts.push(entry.location);
    }

    if (entry.status === 3) {
      parts.push('abgesagt');
    }

    return parts.join(' · ');
  }

  private monthAt(index: number): { year: number; month: number } {
    const absolute = this.startMonth() - 1 + index;
    return {
      year: this.startYear() + Math.floor(absolute / 12),
      month: (absolute % 12) + 1,
    };
  }
}

function daysInMonth(year: number, month: number): number {
  return new Date(Date.UTC(year, month, 0)).getUTCDate();
}
