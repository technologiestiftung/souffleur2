import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

/** Woher die Platzzahl stammt. */
export const CapacitySource = { Unknown: 0, SeatingPlan: 1, Quotas: 2 } as const;

export const TicketingProvider = { None: 0, Pretix: 1 } as const;

export interface Performance {
  key: string;
  name: string;
  date: string;
  capacity: number | null;
  source: number;
  sold: number;
  utilization: number | null;
  revenue: number;
  currency: string;
  capacityNote: string | null;
}

export interface TicketingSettings {
  provider: number;
  key: string;
  baseUrl: string | null;
  organizer: string | null;

  /** Ob ein Token hinterlegt ist -- welches, erfährt die Oberfläche nie. */
  hasToken: boolean;
  cacheSeconds: number;
  isConfigured: boolean;
  lastCheckedAt: string | null;
  lastProblem: string | null;
}

export interface TicketingInput {
  provider: number;
  baseUrl?: string | null;
  organizer?: string | null;

  /** Null lässt das hinterlegte Token stehen. */
  token?: string | null;
  cacheSeconds: number;
}

/**
 * Der Vorverkauf des Hauses.
 *
 * souffleur liest nur: Karten verkauft weiter der Vorverkauf, hier erscheinen
 * bloß die Zahlen daraus. Bei einem gehosteten pretix ist die Schnittstelle
 * ohnehin der einzige Weg -- auf den Server kommt niemand.
 */
@Injectable({ providedIn: 'root' })
export class TicketingService {
  private readonly http = inject(HttpClient);

  settings(): Promise<TicketingSettings> {
    return firstValueFrom(this.http.get<TicketingSettings>('/api/ticketing/settings'));
  }

  save(input: TicketingInput): Promise<TicketingSettings> {
    return firstValueFrom(this.http.put<TicketingSettings>('/api/ticketing/settings', input));
  }

  check(): Promise<TicketingSettings> {
    return firstValueFrom(this.http.post<TicketingSettings>('/api/ticketing/check', {}));
  }

  performances(): Promise<Performance[]> {
    return firstValueFrom(this.http.get<Performance[]>('/api/ticketing/performances'));
  }
}
