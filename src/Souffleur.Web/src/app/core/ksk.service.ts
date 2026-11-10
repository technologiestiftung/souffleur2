import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

/** Was an eine Person gezahlt wurde, und woraus die Zahl stammt. */
export interface ArtistPayments {
  contactId: string;
  name: string;
  amount: number;
  voucherCount: number;

  /** Wie viele Belege noch ungebucht sind -- dort steht brutto. */
  unbookedCount: number;
}

export interface KskReport {
  year: number;
  rate: number | null;
  base: number;
  levy: number | null;
  flagged: ArtistPayments[];
  unflagged: ArtistPayments[];
  unbookedCount: number;
}

export interface KskRate {
  year: number;
  percent: number;
}

/**
 * Die Künstlersozialabgabe.
 *
 * souffleur rechnet zusammen, was das Haus gekennzeichnet hat. Ob jemand
 * abgabepflichtig ist, entscheidet das Haus — das hängt an der Selbständigkeit,
 * an der Art der Leistung und manchmal an einer Einzelfallprüfung der Kasse.
 */
@Injectable({ providedIn: 'root' })
export class KskService {
  private readonly http = inject(HttpClient);

  report(year: number): Promise<KskReport> {
    return firstValueFrom(this.http.get<KskReport>(`/api/personnel/ksk?year=${year}`));
  }

  rates(): Promise<KskRate[]> {
    return firstValueFrom(this.http.get<KskRate[]>('/api/personnel/ksk/rates'));
  }

  setRate(year: number, percent: number): Promise<KskRate> {
    return firstValueFrom(this.http.put<KskRate>(`/api/personnel/ksk/rates/${year}`, { percent }));
  }
}
