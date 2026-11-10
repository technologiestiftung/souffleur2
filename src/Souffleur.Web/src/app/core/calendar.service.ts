import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';

/** Eine Ressource, wie sie am Termin hängt. */
export interface OccurrenceResource {
  id: string;
  name: string;
  display: string;
  color: string | null;
}

/** Wer bei einem Termin dabei ist. */
export interface OccurrenceParticipant {
  contactId: string;
  name: string;
  function: string | null;
  status: number;
  statusLabel: string;
}

/** Eine gewünschte Einteilung, so wie sie zum Server geht. */
export interface ParticipantInput {
  contactId: string;
  functionId?: string | null;
  status?: number;
  note?: string | null;
}

/** Was belegt ist: ein Raum oder eine Person. */
export const ConflictSubject = { Resource: 1, Person: 2 } as const;

/** Ein Termin, der dieselbe Bühne oder Person zur selben Zeit belegt. */
export interface Conflict {
  subject: number;
  subjectId: string;
  subjectName: string;
  localDate: string;
  startsAt: string;
  endsAt: string;
  title: string;
  eventId: string | null;
  seriesId: string | null;
  occurrenceStart: string | null;
  status: number;
  statusLabel: string;
}

/** Woher ein Eintrag stammt. Nur was aus dem Kalender kommt, ist dort änderbar. */
export const CalendarSource = {
  Calendar: 1,
  Tasks: 2,
  Artwork: 3,
  Ticketing: 4,
  Touring: 5,

  /** Jemand fehlt. Nur dass, nicht warum -- den Grund sieht, wer entscheidet. */
  Absence: 6,
} as const;

export interface Occurrence {
  eventId: string | null;

  /** Der Datensatz in der Quelle -- bei einer Frist die Aufgabe. */
  sourceId: string | null;
  seriesId: string | null;
  occurrenceStart: string | null;
  title: string;
  description: string | null;
  location: string | null;
  categoryId: string | null;
  categoryLabel: string | null;
  categoryColor: string | null;
  status: number;
  statusLabel: string;
  startsAt: string;
  endsAt: string;
  localDate: string;
  localEndDate: string;
  spanDays: number;
  isAllDay: boolean;
  isFromSeries: boolean;
  source: number;
  resources: OccurrenceResource[];
  participants: OccurrenceParticipant[];
}

export interface RangeResult {
  from: string;
  to: string;
  timeZone: string;
  occurrences: Occurrence[];
}

export interface EventInput {
  title: string;
  startsAt: string;
  endsAt: string;
  description?: string | null;
  location?: string | null;
  categoryId?: string | null;
  status?: number;
  isAllDay?: boolean;
  resourceIds?: string[] | null;
  participants?: ParticipantInput[] | null;

  /** Zu welcher Produktion der Termin gehört. */
  productionId?: string | null;

  /** Trotz Doppelbelegung anlegen. */
  force?: boolean;
}

export interface SeriesInput extends Omit<EventInput, 'startsAt' | 'endsAt'> {
  startDate: string;
  startTime: string;
  durationMinutes: number;
  frequency: number;
  interval: number;
  byWeekday: number;
  byMonthDay?: number | null;
  bySetPosition?: number | null;
  until?: string | null;
  count?: number | null;
}

/** Ein einzelner Termin, so wie der Bogen zum Bearbeiten ihn braucht. */
export interface EventDetail {
  id: string;
  seriesId: string | null;
  occurrenceStart: string | null;
  title: string;
  description: string | null;
  location: string | null;
  categoryId: string | null;
  status: number;
  startsAt: string;
  endsAt: string;
  isAllDay: boolean;
  resourceIds: string[];
  participants: ParticipantInput[];
  productionId: string | null;
}

/** Eine Serie mit ihrer Regel. */
export interface SeriesDetail {
  id: string;
  title: string;
  description: string | null;
  location: string | null;
  categoryId: string | null;
  status: number;
  startDate: string;
  startTime: string;
  durationMinutes: number;
  frequency: number;
  interval: number;
  byWeekday: number;
  byMonthDay: number | null;
  bySetPosition: number | null;
  until: string | null;
  count: number | null;
  isAllDay: boolean;
  resourceIds: string[];
  participants: ParticipantInput[];
  productionId: string | null;
}

export const EventStatus = { Planned: 1, Confirmed: 2, Cancelled: 3 } as const;

/** Wie fest jemand eingeplant ist. */
export const ParticipationStatus = { Assigned: 1, Accepted: 2, Declined: 3 } as const;

export const Frequency = { Daily: 1, Weekly: 2, Monthly: 3, Yearly: 4 } as const;

/** Als Flags, passend zum Enum auf dem Server. */
export const Weekday = {
  Monday: 1,
  Tuesday: 2,
  Wednesday: 4,
  Thursday: 8,
  Friday: 16,
  Saturday: 32,
  Sunday: 64,
} as const;

@Injectable({ providedIn: 'root' })
export class CalendarService {
  private readonly http = inject(HttpClient);

  private readonly loaded = signal<Occurrence[]>([]);
  private readonly busy = signal(false);

  readonly occurrences = this.loaded.asReadonly();
  readonly loading = this.busy.asReadonly();

  /**
   * Nach Tag sortiert -- das Jahresraster fragt je Zelle, und ohne diese Karte
   * liefe es für jeden der bis zu 744 Tage einmal durch die ganze Liste.
   */
  readonly byDate = computed(() => groupByDate(this.loaded()));

  async range(
    from: string,
    to: string,
    resources: readonly string[] = [],
    people: readonly string[] = [],
  ): Promise<void> {
    this.busy.set(true);
    try {
      let params = new HttpParams().set('from', from).set('to', to);
      for (const id of resources) {
        params = params.append('resources', id);
      }
      for (const id of people) {
        params = params.append('people', id);
      }
      const result = await firstValueFrom(
        this.http.get<RangeResult>('/api/calendar/range', { params }),
      );
      this.loaded.set(result.occurrences);
    } catch {
      // Modul abgeschaltet oder nicht angemeldet: dann gibt es nichts zu zeigen.
      this.loaded.set([]);
    } finally {
      this.busy.set(false);
    }
  }

  /** Ein Zeitraum zum Auszählen, ohne den angezeigten Kalender zu verstellen. */
  async occurrencesIn(from: string, to: string): Promise<Occurrence[]> {
    const params = new HttpParams().set('from', from).set('to', to);
    const result = await firstValueFrom(
      this.http.get<RangeResult>('/api/calendar/range', { params }),
    );
    return result.occurrences;
  }

  createEvent(input: EventInput): Promise<unknown> {
    return firstValueFrom(this.http.post('/api/calendar/events', input));
  }

  getEvent(id: string): Promise<EventDetail> {
    return firstValueFrom(this.http.get<EventDetail>(`/api/calendar/events/${id}`));
  }

  getSeries(id: string): Promise<SeriesDetail> {
    return firstValueFrom(this.http.get<SeriesDetail>(`/api/calendar/series/${id}`));
  }

  updateEvent(id: string, input: EventInput): Promise<unknown> {
    return firstValueFrom(this.http.put(`/api/calendar/events/${id}`, input));
  }

  /**
   * Jemanden an einem bestehenden Termin einteilen.
   *
   * Eigener Weg neben updateEvent: Der Dienstplan fügt eine Person hinzu und
   * kennt den ganzen Termin nicht — was beim vollen Bogen fehlte, wäre danach
   * gelöscht.
   */
  assign(
    eventId: string,
    contactId: string,
    functionId: string | null,
    force = false,
  ): Promise<unknown> {
    return firstValueFrom(
      this.http.post(`/api/calendar/events/${eventId}/participants`, {
        contactId,
        functionId,
        force,
      }),
    );
  }

  unassign(eventId: string, contactId: string): Promise<unknown> {
    return firstValueFrom(
      this.http.delete(`/api/calendar/events/${eventId}/participants/${contactId}`),
    );
  }

  deleteEvent(id: string): Promise<unknown> {
    return firstValueFrom(this.http.delete(`/api/calendar/events/${id}`));
  }

  createSeries(input: SeriesInput): Promise<unknown> {
    return firstValueFrom(this.http.post('/api/calendar/series', input));
  }

  /** Die Regel ändern. Einzeln verschobene Termine bleiben, wie sie sind. */
  updateSeries(id: string, input: SeriesInput): Promise<unknown> {
    return firstValueFrom(this.http.put(`/api/calendar/series/${id}`, input));
  }

  deleteSeries(id: string): Promise<unknown> {
    return firstValueFrom(this.http.delete(`/api/calendar/series/${id}`));
  }

  /**
   * Nur diesen einen Termin einer Serie ändern. Legt die Abweichung an oder
   * ändert die vorhandene; Räume und Einteilung der Serie gehen mit.
   */
  overrideOccurrence(seriesId: string, start: string, input: EventInput): Promise<unknown> {
    return firstValueFrom(
      this.http.put(
        `/api/calendar/series/${seriesId}/occurrences/${encodeURIComponent(start)}`,
        input,
      ),
    );
  }

  /**
   * Vorab fragen, ob die Ressourcen frei sind -- bevor jemand auf „Anlegen"
   * drückt und erst dann erfährt, dass die Bühne belegt ist.
   */
  async conflicts(request: {
    resourceIds: string[];
    contactIds?: string[];
    startsAt?: string;
    endsAt?: string;
    series?: SeriesInput;
    ignoreEventId?: string;
    ignoreSeriesId?: string;
  }): Promise<Conflict[]> {
    if (request.resourceIds.length === 0 && (request.contactIds?.length ?? 0) === 0) {
      return [];
    }

    const answer = await firstValueFrom(
      this.http.post<{ conflicts: Conflict[] }>('/api/calendar/conflicts', request),
    );
    return answer.conflicts;
  }

  cancelOccurrence(seriesId: string, start: string): Promise<unknown> {
    return firstValueFrom(
      this.http.post(
        `/api/calendar/series/${seriesId}/occurrences/${encodeURIComponent(start)}/cancel`,
        {},
      ),
    );
  }
}

/**
 * Termine nach Tag ordnen -- das Jahresraster fragt je Zelle, und ohne diese
 * Karte liefe es für jeden der bis zu 744 Tage einmal durch die ganze Liste.
 */
export function groupByDate(occurrences: readonly Occurrence[]): Map<string, Occurrence[]> {
  const map = new Map<string, Occurrence[]>();

  for (const occurrence of occurrences) {
    // Ein mehrtägiger Termin gehört in jeden Tag, über den er läuft: Ein
    // Gastspiel steht im Raster vom Anreise- bis zum Abreisetag.
    for (const day of daysBetween(occurrence.localDate, occurrence.localEndDate)) {
      const list = map.get(day);
      if (list) {
        list.push(occurrence);
      } else {
        map.set(day, [occurrence]);
      }
    }
  }

  return map;
}

/** Alle Tage von einschließlich bis einschließlich, als ISO-Datum. */
export function daysBetween(from: string, to: string): string[] {
  const days: string[] = [from];
  let current = from;

  // Ohne Deckel würde ein fehlerhaftes Enddatum die Schleife nie verlassen.
  while (current < to && days.length < 400) {
    current = addDays(current, 1);
    days.push(current);
  }

  return days;
}

/**
 * Datumsrechnung in UTC, damit die Sommerzeit nicht hineinredet: Hier geht es
 * um Kalendertage, nicht um Zeitpunkte.
 */
export function addDays(date: string, count: number): string {
  const value = new Date(`${date}T00:00:00Z`);
  value.setUTCDate(value.getUTCDate() + count);
  return value.toISOString().slice(0, 10);
}

export function isoDate(year: number, month: number, day: number): string {
  return `${year}-${String(month).padStart(2, '0')}-${String(day).padStart(2, '0')}`;
}
