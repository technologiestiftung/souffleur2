import { Component, computed, input, output } from '@angular/core';
import { Occurrence, isoDate } from '../../core/calendar.service';

/** Ein Termin mit seinem Platz im Stundenraster. */
interface Placed {
  readonly entry: Occurrence;
  readonly top: number;
  readonly height: number;
  readonly column: number;
  readonly columns: number;
  readonly startsEarlier: boolean;
  readonly endsLater: boolean;
}

interface Column {
  readonly date: string;
  readonly weekday: string;
  readonly number: number;
  readonly isWeekend: boolean;
  readonly isToday: boolean;
  readonly allDay: readonly Occurrence[];
  readonly placed: readonly Placed[];
}

const WEEKDAYS = ['So', 'Mo', 'Di', 'Mi', 'Do', 'Fr', 'Sa'];

/** Pixel je Stunde. Kleiner wird eine Probe von 90 Minuten unleserlich. */
const HOUR = 44;

/**
 * Woche und Tag im Stundenraster -- eine Komponente für beides, weil der Tag
 * nichts anderes ist als eine Woche mit einer Spalte.
 *
 * Hier zählt, was das Jahresraster nicht zeigen kann: wie lange etwas dauert
 * und was sich überschneidet. Zwei Proben zur selben Zeit stehen nebeneinander
 * statt übereinander -- sonst wäre die Kollision genau das, was man übersieht.
 */
@Component({
  selector: 'souffleur-time-grid',
  templateUrl: './time-grid.component.html',
  host: { class: 'block h-full min-h-0' },
})
export class TimeGridComponent {
  readonly days = input.required<readonly string[]>();
  readonly byDate = input.required<Map<string, Occurrence[]>>();

  readonly dayPicked = output<string>();
  readonly entryPicked = output<Occurrence>();

  protected readonly hourHeight = HOUR;

  private readonly today = isoDate(
    new Date().getFullYear(),
    new Date().getMonth() + 1,
    new Date().getDate(),
  );

  /**
   * Der gezeigte Ausschnitt des Tages. Nicht fest 0 bis 24: Der halbe
   * Bildschirm ginge für Stunden drauf, in denen im Haus nichts passiert.
   * Gezeigt wird 8 bis 23 und, wo nötig, mehr.
   */
  protected readonly hours = computed<number[]>(() => {
    let first = 8;
    let last = 23;

    for (const date of this.days()) {
      for (const entry of this.byDate().get(date) ?? []) {
        if (this.isBanner(entry)) {
          continue;
        }

        const { startMinute, endMinute } = this.clip(entry, date);
        first = Math.min(first, Math.floor(startMinute / 60));
        last = Math.max(last, Math.ceil(endMinute / 60));
      }
    }

    return Array.from({ length: Math.min(24, last) - first + 1 }, (_, i) => first + i);
  });

  protected readonly columns = computed<Column[]>(() =>
    this.days().map((date) => {
      const entries = this.byDate().get(date) ?? [];
      const parts = date.split('-').map(Number);
      const weekday = new Date(`${date}T00:00:00Z`).getUTCDay();

      return {
        date,
        weekday: WEEKDAYS[weekday],
        number: parts[2],
        isWeekend: weekday === 0 || weekday === 6,
        isToday: date === this.today,
        allDay: entries.filter((e) => this.isBanner(e)),
        placed: this.place(entries.filter((e) => !this.isBanner(e)), date),
      };
    }),
  );

  /** Wie hoch das Raster insgesamt ist -- daraus rechnen sich alle Abstände. */
  protected readonly bodyHeight = computed(() => this.hours().length * HOUR);

  protected readonly hasBanners = computed(() =>
    this.columns().some((c) => c.allDay.length > 0),
  );

  /**
   * Wo im Raster die aktuelle Uhrzeit liegt, oder null, wenn sie ausserhalb
   * des gezeigten Ausschnitts faellt.
   */
  protected readonly nowOffset = computed<number | null>(() => {
    const first = this.hours()[0];
    const last = this.hours()[this.hours().length - 1];
    if (first === undefined) {
      return null;
    }

    const now = new Date();
    const minutes = now.getHours() * 60 + now.getMinutes();

    return minutes < first * 60 || minutes > last * 60
      ? null
      : ((minutes - first * 60) / 60) * HOUR;
  });

  protected wash(color: string | null): string {
    return color
      ? `color-mix(in oklab, ${color} 22%, var(--color-surface-raised))`
      : 'var(--color-surface-sunken)';
  }

  protected accent(color: string | null): string {
    return color ?? 'var(--color-border-strong)';
  }

  protected label(hour: number): string {
    return `${String(hour).padStart(2, '0')}:00`;
  }

  protected dayLabel(column: Column): string {
    return new Date(`${column.date}T12:00:00Z`).toLocaleDateString('de-DE', {
      weekday: 'long',
      day: 'numeric',
      month: 'long',
    });
  }

  protected timeRange(entry: Occurrence): string {
    const format = (value: string) =>
      new Date(value).toLocaleTimeString('de-DE', { hour: '2-digit', minute: '2-digit' });
    return `${format(entry.startsAt)} – ${format(entry.endsAt)}`;
  }

  /**
   * Ganztägiges und Mehrtägiges gehört in das Band über dem Raster. Ein
   * viertägiges Gastspiel als Balken durch den Stundenplan zu ziehen hieße,
   * die Spalte für alles andere zu verlieren.
   */
  private isBanner(entry: Occurrence): boolean {
    return entry.isAllDay || entry.spanDays > 1;
  }

  /**
   * Anfang und Ende auf den gezeigten Tag beschneiden -- ein Termin über
   * Mitternacht gehört in beide Spalten, aber in jede nur so weit, wie er
   * dort liegt.
   */
  private clip(entry: Occurrence, date: string): { startMinute: number; endMinute: number } {
    const dayStart = new Date(`${date}T00:00:00`).getTime();
    const dayEnd = dayStart + 24 * 60 * 60 * 1000;

    const start = Math.max(new Date(entry.startsAt).getTime(), dayStart);
    const end = Math.min(new Date(entry.endsAt).getTime(), dayEnd);

    return {
      startMinute: (start - dayStart) / 60000,
      endMinute: Math.max((end - dayStart) / 60000, (start - dayStart) / 60000 + 15),
    };
  }

  /**
   * Überlappende Termine nebeneinander legen.
   *
   * Erst werden Trauben gebildet -- Termine, die sich über eine Kette von
   * Überschneidungen berühren --, dann bekommt jeder die erste Spalte, in der
   * nichts mehr läuft. Die Breite richtet sich nach der Traube, nicht nach dem
   * ganzen Tag: Zwei Termine um zehn sollen nicht schmal werden, weil abends
   * vier gleichzeitig laufen.
   */
  private place(entries: readonly Occurrence[], date: string): Placed[] {
    const first = this.hours()[0] ?? 0;
    const offset = first * 60;

    const prepared = entries
      .map((entry) => ({ entry, ...this.clip(entry, date) }))
      .sort((a, b) => a.startMinute - b.startMinute || b.endMinute - a.endMinute);

    const result: Placed[] = [];
    let cluster: typeof prepared = [];
    let clusterEnd = -1;

    const flush = () => {
      if (cluster.length === 0) {
        return;
      }

      const columnEnds: number[] = [];
      const assigned = cluster.map((item) => {
        let column = columnEnds.findIndex((end) => end <= item.startMinute);
        if (column === -1) {
          column = columnEnds.length;
        }
        columnEnds[column] = item.endMinute;
        return { item, column };
      });

      for (const { item, column } of assigned) {
        const dayStart = new Date(`${date}T00:00:00`).getTime();
        result.push({
          entry: item.entry,
          top: ((item.startMinute - offset) / 60) * HOUR,
          height: Math.max(((item.endMinute - item.startMinute) / 60) * HOUR, 18),
          column,
          columns: columnEnds.length,
          startsEarlier: new Date(item.entry.startsAt).getTime() < dayStart,
          endsLater: new Date(item.entry.endsAt).getTime() > dayStart + 24 * 60 * 60 * 1000,
        });
      }

      cluster = [];
      clusterEnd = -1;
    };

    for (const item of prepared) {
      if (cluster.length > 0 && item.startMinute >= clusterEnd) {
        flush();
      }

      cluster.push(item);
      clusterEnd = Math.max(clusterEnd, item.endMinute);
    }

    flush();
    return result;
  }
}
