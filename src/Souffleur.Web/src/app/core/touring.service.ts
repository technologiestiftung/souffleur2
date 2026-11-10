import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

/** Wie weit ein Gastspiel ist -- dieselben Zahlen wie am Server. */
export const TourStopStatus = {
  Inquiry: 1,
  Offered: 2,
  Confirmed: 3,
  Cancelled: 4,
  Settled: 5,
} as const;

export interface StatusInfo {
  status: number;
  key: string;
  label: string;
  isBinding: boolean;
}

export interface TourMember {
  id: string;
  contactId: string;
  contactName: string;
  functionId: string | null;
  functionLabel: string | null;
  status: number;
  statusLabel: string;
  note: string | null;
}

export interface TourPerformance {
  id: string;
  startsAt: string;
  endsAt: string;
  note: string | null;
}

export interface TourStop {
  id: string;
  tourId: string;
  title: string;
  venueId: string;
  venueName: string;
  venueCity: string | null;
  contactPersonId: string | null;
  contactPersonName: string | null;
  status: number;
  statusLabel: string;
  isBinding: boolean;
  arrivalDate: string | null;
  departureDate: string | null;
  fee: number | null;
  hostCoversTravel: boolean;
  hostCoversAccommodation: boolean;
  voucherId: string | null;
  voucherLabel: string | null;
  notes: string | null;
  performances: TourPerformance[];
}

export interface Tour {
  id: string;
  title: string;
  productionId: string | null;
  productionLabel: string | null;
  notes: string | null;
  startsOn: string | null;
  endsOn: string | null;
  stops: TourStop[];
  members: TourMember[];
}

export interface MemberInput {
  contactId: string;
  functionId?: string | null;
  status?: number | null;
  note?: string | null;
}

export interface TourInput {
  title: string;
  productionId?: string | null;
  notes?: string | null;
  members?: MemberInput[] | null;
}

export interface PerformanceInput {
  startsAt: string;
  endsAt: string;
  note?: string | null;
}

export interface StopInput {
  venueId: string;
  contactPersonId?: string | null;
  status?: number | null;
  arrivalDate?: string | null;
  departureDate?: string | null;
  fee?: number | null;
  hostCoversTravel?: boolean;
  hostCoversAccommodation?: boolean;
  voucherId?: string | null;
  notes?: string | null;
  performances?: PerformanceInput[] | null;
}

/**
 * Tourneen und Gastspiele des Hauses.
 *
 * Alles, was hier steht, verweist auf andere Module: Die Spielstätte ist ein
 * Kontakt, die Vorstellungen erscheinen im Kalender, die Gage hängt an einem
 * Beleg. Dieser Dienst kennt nur die Tournee -- die Verweise löst die
 * Oberfläche über die Dienste der jeweiligen Module auf.
 */
@Injectable({ providedIn: 'root' })
export class TouringService {
  private readonly http = inject(HttpClient);

  statuses(): Promise<StatusInfo[]> {
    return firstValueFrom(this.http.get<StatusInfo[]>('/api/touring/statuses'));
  }

  tours(includeSettled = true): Promise<Tour[]> {
    return firstValueFrom(
      this.http.get<Tour[]>('/api/touring/tours', { params: { includeSettled } }),
    );
  }

  createTour(input: TourInput): Promise<Tour> {
    return firstValueFrom(this.http.post<Tour>('/api/touring/tours', input));
  }

  updateTour(id: string, input: TourInput): Promise<Tour> {
    return firstValueFrom(this.http.put<Tour>(`/api/touring/tours/${id}`, input));
  }

  removeTour(id: string): Promise<void> {
    return firstValueFrom(this.http.delete<void>(`/api/touring/tours/${id}`));
  }

  createStop(tourId: string, input: StopInput): Promise<TourStop> {
    return firstValueFrom(this.http.post<TourStop>(`/api/touring/tours/${tourId}/stops`, input));
  }

  updateStop(id: string, input: StopInput): Promise<TourStop> {
    return firstValueFrom(this.http.put<TourStop>(`/api/touring/stops/${id}`, input));
  }

  removeStop(id: string): Promise<void> {
    return firstValueFrom(this.http.delete<void>(`/api/touring/stops/${id}`));
  }
}
