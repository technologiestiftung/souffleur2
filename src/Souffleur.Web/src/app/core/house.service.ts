import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { OrganizationInfo } from './api';

export interface House {
  name: string;
  chartLabel: string;
  isNonProfit: boolean;
  hasDemoData: boolean;
}

/** Die Stammdaten des Hauses, wie die Einstellungsseite sie zeigt und ändert. */
export interface Stammdaten {
  name: string;
  legalForm: string | null;
  street: string | null;
  postalCode: string | null;
  city: string | null;
  taxNumber: string | null;
  vatId: string | null;
  kskNumber: string | null;
  chartOfAccounts: number;
  chartLabel: string;
  isNonProfit: boolean;
  requiresTaxSpheres: boolean;
  hasDemoData: boolean;
}

export type StammdatenEingabe = Omit<
  Stammdaten,
  'chartLabel' | 'requiresTaxSpheres' | 'hasDemoData'
>;

export interface Kontenrahmen {
  chart: number;
  key: string;
  label: string;
  usesTaxSpheres: boolean;
}

export interface Sphaere {
  sphere: number;
  key: string;
  label: string;
  example: string;
}

/** Das Haus, das diese Installation betreibt. */
@Injectable({ providedIn: 'root' })
export class HouseService {
  private readonly http = inject(HttpClient);

  private readonly loaded = signal<House | null>(null);

  readonly house = this.loaded.asReadonly();

  /**
   * Ob hier Erfundenes steht. Die Kennzeichnung gehört sichtbar in die
   * Oberfläche: Einem Termin sieht man nicht an, dass er aus einem
   * Vorführbestand stammt -- erst recht nicht, wenn er im eigenen Telefon
   * gelandet ist.
   */
  readonly hasDemoData = computed(() => this.loaded()?.hasDemoData ?? false);

  async load(): Promise<void> {
    try {
      this.loaded.set(await firstValueFrom(this.http.get<House>('/api/organization')));
    } catch {
      // Nicht angemeldet oder noch nicht eingerichtet: dann gibt es nichts
      // zu kennzeichnen.
      this.loaded.set(null);
    }
  }

  /** Die vollen Stammdaten, wie die Übersicht sie zeigt -- ohne den Zustand oben anzufassen. */
  organization(): Promise<OrganizationInfo> {
    return firstValueFrom(this.http.get<OrganizationInfo>('/api/organization'));
  }

  stammdaten(): Promise<Stammdaten> {
    return firstValueFrom(this.http.get<Stammdaten>('/api/organization'));
  }

  /** Speichert und frischt zugleich auf, was die Kopfzeile vom Haus zeigt. */
  async speichere(eingabe: StammdatenEingabe): Promise<Stammdaten> {
    const gespeichert = await firstValueFrom(
      this.http.put<Stammdaten>('/api/organization', eingabe),
    );
    this.loaded.set(gespeichert);
    return gespeichert;
  }

  kontenrahmen(): Promise<Kontenrahmen[]> {
    return firstValueFrom(this.http.get<Kontenrahmen[]>('/api/organization/charts-of-accounts'));
  }

  sphaeren(): Promise<Sphaere[]> {
    return firstValueFrom(this.http.get<Sphaere[]>('/api/organization/tax-spheres'));
  }
}
