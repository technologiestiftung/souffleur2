import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

export interface RoleOnContact {
  role: number;
  key: string;
  label: string;
}

export interface ContactBankDetails {
  iban: string | null;
  bic: string | null;
  paymentTermDays: number | null;
}

export interface Contact {
  id: string;
  kind: number;
  name: string;
  givenName: string | null;
  surname: string | null;
  position: string | null;
  email: string | null;
  phone: string | null;
  street: string | null;
  postalCode: string | null;
  city: string | null;
  notes: string | null;
  isActive: boolean;
  mayBeContacted: boolean;
  contactConsentAt: string | null;

  /** Das Anmeldekonto dieser Person, falls sie eines hat. */
  userId: string | null;
  roles: RoleOnContact[];
  bankDetails: ContactBankDetails | null;
}

/** Was beim Anlegen und Ändern zum Server geht. */
export interface ContactInput {
  kind: number;
  roles: number[];
  name?: string | null;
  givenName?: string | null;
  surname?: string | null;
  position?: string | null;
  email?: string | null;
  phone?: string | null;
  street?: string | null;
  postalCode?: string | null;
  city?: string | null;
  notes?: string | null;
  contactConsentAt?: string | null;

  /** Nur die Verwaltung darf das setzen; der Server weist andere ab. */
  userId?: string | null;
  bankDetails?: {
    iban?: string | null;
    bic?: string | null;
    paymentTermDays?: number | null;
  } | null;
}

/** Ein Anmeldekonto, zum Verbinden mit einem Kontakt. */
export interface UserAccount {
  id: string;
  name: string;
  email: string | null;
  isActive: boolean;

  /** An welchem Kontakt es schon hängt -- null heißt frei. */
  linkedTo: string | null;
}

export interface RoleInfo {
  role: number;
  key: string;
  label: string;
  needsConsent: boolean;
  needsBankDetails: boolean;
}

export const ContactKind = { Organization: 1, Person: 2 } as const;

/**
 * Das Kontaktmodul mit allem, was dazugehört. Für Name und Funktion nebenbei
 * -- Einteilung, Zuständigkeit, Lieferant -- reicht der PeopleService.
 */
@Injectable({ providedIn: 'root' })
export class ContactsService {
  private readonly http = inject(HttpClient);

  list(): Promise<Contact[]> {
    return firstValueFrom(this.http.get<Contact[]>('/api/contacts?take=500'));
  }

  roles(): Promise<RoleInfo[]> {
    return firstValueFrom(this.http.get<RoleInfo[]>('/api/contacts/roles'));
  }

  /** Die Anmeldekonten. Nur die Verwaltung bekommt sie -- sonst 403. */
  accounts(): Promise<UserAccount[]> {
    return firstValueFrom(this.http.get<UserAccount[]>('/api/contacts/accounts'));
  }

  create(input: ContactInput): Promise<Contact> {
    return firstValueFrom(this.http.post<Contact>('/api/contacts', input));
  }

  update(id: string, input: ContactInput): Promise<Contact> {
    return firstValueFrom(this.http.put<Contact>(`/api/contacts/${id}`, input));
  }

  /**
   * Aus dem Verkehr ziehen, nicht löschen: Buchungen und Belege verweisen
   * aufbewahrungspflichtig auf den Kontakt.
   */
  async deactivate(id: string): Promise<void> {
    await firstValueFrom(this.http.post(`/api/contacts/${id}/deactivate`, {}));
  }
}
