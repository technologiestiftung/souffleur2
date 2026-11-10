import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';

export interface Subscription {
  id: string;
  name: string;
  token: string;
  address: string;
  categoryIds: string[];
  resourceIds: string[];
  contactIds: string[];
  includeCancelled: boolean;
  isActive: boolean;
  lastFetchedAt: string | null;
  fetchCount: number;
}

export interface SubscriptionInput {
  name: string;
  categoryIds?: string[];
  resourceIds?: string[];
  contactIds?: string[];
  includeCancelled?: boolean;
  isActive?: boolean;
}

/**
 * Abonnements auf den Kalender -- siehe ADR-0006.
 *
 * Kein Export: Die Adresse wird in der Kalenderanwendung eingetragen und dort
 * regelmäßig abgefragt. Was sich in souffleur ändert, ändert sich beim
 * nächsten Abgleich auch im Telefon.
 */
@Injectable({ providedIn: 'root' })
export class SubscriptionsService {
  private readonly http = inject(HttpClient);

  private readonly loaded = signal<Subscription[]>([]);

  readonly subscriptions = this.loaded.asReadonly();

  async load(): Promise<Subscription[]> {
    const found = await firstValueFrom(
      this.http.get<Subscription[]>('/api/calendar/subscriptions'),
    );
    this.loaded.set(found);
    return found;
  }

  create(input: SubscriptionInput): Promise<Subscription> {
    return firstValueFrom(
      this.http.post<Subscription>('/api/calendar/subscriptions', input),
    );
  }

  update(id: string, input: SubscriptionInput): Promise<Subscription> {
    return firstValueFrom(
      this.http.put<Subscription>(`/api/calendar/subscriptions/${id}`, input),
    );
  }

  /** Neues Token: Die alte Adresse antwortet ab sofort nicht mehr. */
  rotate(id: string): Promise<Subscription> {
    return firstValueFrom(
      this.http.post<Subscription>(`/api/calendar/subscriptions/${id}/rotate`, {}),
    );
  }

  async remove(id: string): Promise<void> {
    await firstValueFrom(this.http.delete(`/api/calendar/subscriptions/${id}`));
  }
}

/**
 * Dieselbe Adresse mit `webcal:` -- darauf reagieren Apple Kalender, Outlook
 * und Thunderbird mit „abonnieren" statt mit „herunterladen".
 */
export function webcal(address: string): string {
  return address.replace(/^https?:/, 'webcal:');
}
