import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';

export const TimeSource = { Clock: 1, Manual: 2 } as const;

export interface TimeEntry {
  id: string;
  contactId: string;
  contactName: string | null;
  startedAt: string;
  endedAt: string | null;
  breakMinutes: number;
  workedMinutes: number | null;
  functionId: string | null;
  functionLabel: string | null;
  eventId: string | null;
  note: string | null;
  source: number;
  sourceLabel: string;
  correctionReason: string | null;

  /** Was auffällt, ohne den Eintrag ungültig zu machen -- Pausen, zu lange Tage. */
  notices: string[];
}

/** Wer ich bin und ob gerade etwas läuft. */
export interface MyClock {
  contactId: string | null;
  name: string | null;
  running: TimeEntry | null;
}

export interface TimeInput {
  contactId?: string | null;
  startedAt: string;
  endedAt: string | null;
  breakMinutes: number;
  functionId?: string | null;
  eventId?: string | null;
  note?: string | null;
}

/**
 * Kommen und Gehen.
 *
 * Die Tagesgrenzen zieht die Oberfläche, nicht der Server: Ein Zeitpunkt ist
 * ein Zeitpunkt, erst eine Zeitzone macht einen Tag daraus. Für ein Haus,
 * dessen Leute vor Ort sitzen, ist das die Zeitzone des Browsers.
 */
@Injectable({ providedIn: 'root' })
export class TimesService {
  private readonly http = inject(HttpClient);

  /** Der eigene Stand, damit die Aktionsleiste weiß, was sie anbieten soll. */
  readonly clock = signal<MyClock | null>(null);

  async me(): Promise<MyClock> {
    const mine = await firstValueFrom(this.http.get<MyClock>('/api/personnel/times/me'));
    this.clock.set(mine);
    return mine;
  }

  async clockIn(functionId?: string | null, note?: string | null): Promise<TimeEntry> {
    const entry = await firstValueFrom(
      this.http.post<TimeEntry>('/api/personnel/times/kommen', {
        functionId: functionId ?? null,
        note: note ?? null,
      }),
    );

    await this.me();
    return entry;
  }

  async clockOut(breakMinutes: number): Promise<TimeEntry> {
    const entry = await firstValueFrom(
      this.http.post<TimeEntry>('/api/personnel/times/gehen', { breakMinutes }),
    );

    await this.me();
    return entry;
  }

  async discard(): Promise<void> {
    await firstValueFrom(this.http.delete<void>('/api/personnel/times/laufend'));
    await this.me();
  }

  list(from: Date, to: Date, contactId?: string | null, all = false): Promise<TimeEntry[]> {
    const query = new URLSearchParams({ from: from.toISOString(), to: to.toISOString() });

    if (contactId) {
      query.set('contactId', contactId);
    }

    if (all) {
      query.set('all', 'true');
    }

    return firstValueFrom(this.http.get<TimeEntry[]>(`/api/personnel/times?${query}`));
  }

  add(input: TimeInput): Promise<TimeEntry> {
    return firstValueFrom(this.http.post<TimeEntry>('/api/personnel/times', input));
  }

  correct(id: string, entry: TimeInput, reason: string): Promise<TimeEntry> {
    return firstValueFrom(
      this.http.put<TimeEntry>(`/api/personnel/times/${id}`, { entry, reason }),
    );
  }
}

/** Minuten als „7:30" -- so steht es auf jedem Stundenzettel. */
export function asHours(minutes: number): string {
  const sign = minutes < 0 ? '−' : '';
  const absolute = Math.abs(minutes);
  return `${sign}${Math.floor(absolute / 60)}:${String(absolute % 60).padStart(2, '0')}`;
}
