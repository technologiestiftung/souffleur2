import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { Account, CostCenter, JournalEntry } from './api';

/** Eine Zeile, wie sie zum Server geht. */
export interface JournalLineInput {
  accountId: string;
  debit: number;
  credit: number;
  taxKey?: string | null;
  taxAmount?: number;
  taxSphere?: number | null;
  costCenterId?: string | null;
  contactId?: string | null;
  description?: string | null;
}

export interface JournalEntryInput {
  fiscalYear: number;
  date: string;
  description: string;
  lines: JournalLineInput[];
  documentNumber?: string | null;
}

/** Was ein DATEV-Stapel umfassen würde -- und was ihm im Weg steht. */
export interface DatevPreview {
  from: string;
  to: string;
  entryCount: number;
  alreadyCommitted: number;
  debitTotal: number;
  problems: string[];
  canExport: boolean;
}

/**
 * Die steuerlichen Sphären. Nur gemeinnützige Häuser mit Sphärentrennung
 * brauchen sie -- siehe OrganizationInfo.requiresTaxSpheres.
 */
export const TAX_SPHERES = [
  { value: 1, label: 'Ideeller Bereich' },
  { value: 2, label: 'Vermögensverwaltung' },
  { value: 3, label: 'Zweckbetrieb' },
  { value: 4, label: 'Wirtschaftlicher Geschäftsbetrieb' },
] as const;

/**
 * Das Journal und seine Stammdaten.
 *
 * Kein eigener Zustand: Übersicht, Journal und Belege brauchen jeweils einen
 * anderen Ausschnitt. Ein abgeschaltetes Modul Finanzen antwortet mit 404 --
 * was das für die Seite heißt, entscheidet die Seite.
 */
@Injectable({ providedIn: 'root' })
export class JournalService {
  private readonly http = inject(HttpClient);

  /** Die jüngsten Buchungen. */
  entries(take: number): Promise<JournalEntry[]> {
    const params = new HttpParams().set('take', take);
    return firstValueFrom(this.http.get<JournalEntry[]>('/api/accounting/journal', { params }));
  }

  accounts(): Promise<Account[]> {
    return firstValueFrom(this.http.get<Account[]>('/api/accounting/accounts'));
  }

  costCenters(): Promise<CostCenter[]> {
    return firstValueFrom(this.http.get<CostCenter[]>('/api/accounting/cost-centers'));
  }

  /** Legt eine Buchung an. Sie entsteht als Entwurf. */
  create(input: JournalEntryInput): Promise<JournalEntry> {
    return firstValueFrom(this.http.post<JournalEntry>('/api/accounting/journal', input));
  }

  /** Schreibt sie fest. Danach ist sie unveränderlich (§ 146 Abs. 4 AO). */
  commit(id: string): Promise<JournalEntry> {
    return firstValueFrom(
      this.http.post<JournalEntry>(`/api/accounting/journal/${id}/commit`, {}),
    );
  }

  datevPreview(from: string, to: string): Promise<DatevPreview> {
    const params = new HttpParams().set('from', from).set('to', to);
    return firstValueFrom(
      this.http.get<DatevPreview>('/api/accounting/datev/preview', { params }),
    );
  }

  /**
   * Erzeugt den Stapel und gibt ihn als Datei zurück. Dabei werden die
   * enthaltenen Buchungen festgeschrieben -- das ist der Zweck, keine
   * Nebenwirkung.
   */
  async datevExport(from: string, to: string): Promise<{ file: Blob; name: string }> {
    const params = new HttpParams().set('from', from).set('to', to);
    const answer = await firstValueFrom(
      this.http.post('/api/accounting/datev/export', null, {
        params,
        observe: 'response',
        responseType: 'blob',
      }),
    );

    const disposition = answer.headers.get('content-disposition') ?? '';
    const match = /filename\*?=(?:UTF-8'')?"?([^";]+)"?/i.exec(disposition);

    return {
      file: answer.body ?? new Blob(),
      name: match ? decodeURIComponent(match[1]) : 'datev-stapel.csv',
    };
  }
}
