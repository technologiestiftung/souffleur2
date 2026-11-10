import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { Account, CostCenter } from './api';

/** Die Kontenarten, dieselben Zahlen wie am Server. */
export const AccountType = {
  Asset: 1,
  Liability: 2,
  Equity: 3,
  Revenue: 4,
  Expense: 5,
  Receivable: 6,
  Payable: 7,
} as const;

export const ACCOUNT_TYPES = [
  { value: AccountType.Asset, label: 'Bestand (Aktiv)' },
  { value: AccountType.Liability, label: 'Verbindlichkeit' },
  { value: AccountType.Equity, label: 'Eigenkapital' },
  { value: AccountType.Revenue, label: 'Erlös' },
  { value: AccountType.Expense, label: 'Aufwand' },
  { value: AccountType.Receivable, label: 'Debitor' },
  { value: AccountType.Payable, label: 'Kreditor' },
] as const;

export interface AccountInput {
  number: string;
  name: string;
  type: number;
  taxKey: string | null;
  defaultTaxSphere: number | null;
}

export interface AccountChange {
  name: string;
  type: number;
  taxKey: string | null;
  defaultTaxSphere: number | null;
  isActive: boolean;
}

export interface ImportResult {
  added: number;
  skippedBecauseTheyExist: string[];
  rejected: string[];
}

/**
 * Kontenplan und Kostenstellen des Hauses. souffleur liefert keine Konten
 * mit -- welche gelten, sagt das Steuerbüro.
 */
@Injectable({ providedIn: 'root' })
export class KontenplanService {
  private readonly http = inject(HttpClient);

  accounts(includeInactive = true): Promise<Account[]> {
    const params = new HttpParams().set('includeInactive', includeInactive);
    return firstValueFrom(this.http.get<Account[]>('/api/accounting/accounts', { params }));
  }

  create(input: AccountInput): Promise<Account> {
    return firstValueFrom(this.http.post<Account>('/api/accounting/accounts', input));
  }

  update(id: string, change: AccountChange): Promise<Account> {
    return firstValueFrom(this.http.put<Account>(`/api/accounting/accounts/${id}`, change));
  }

  import(accounts: AccountInput[]): Promise<ImportResult> {
    return firstValueFrom(
      this.http.post<ImportResult>('/api/accounting/accounts/import', { accounts }),
    );
  }

  costCenters(): Promise<CostCenter[]> {
    return firstValueFrom(this.http.get<CostCenter[]>('/api/accounting/cost-centers'));
  }

  createCostCenter(number: string, name: string): Promise<CostCenter> {
    return firstValueFrom(
      this.http.post<CostCenter>('/api/accounting/cost-centers', { number, name }),
    );
  }
}

/** Eine Zeile aus der Datei des Steuerbüros, vor dem Einlesen. */
export interface CsvRow {
  number: string;
  name: string;
  type: number | null;
  taxKey: string | null;
}

/**
 * Die Kontenliste des Steuerbüros lesen: `Nummer;Name;Art;Steuerschlüssel`,
 * Art und Steuerschlüssel dürfen fehlen. Eine Kopfzeile wird erkannt und
 * übergangen; Komma statt Semikolon geht auch, wenn kein Semikolon vorkommt.
 *
 * Die Art wird nicht aus der Nummer geraten: Wo sie fehlt, wählt man sie in
 * der Vorschau. Ein falsch geratenes Konto stünde sonst auf der falschen
 * Seite der Bilanz, und niemand merkte es.
 */
export function parseKontenCsv(text: string): CsvRow[] {
  const lines = text
    .replace(/^﻿/, '')
    .split(/\r?\n/)
    .map((line) => line.trim())
    .filter((line) => line.length > 0);

  const separator = lines.some((line) => line.includes(';')) ? ';' : ',';
  const rows = lines.map((line) => line.split(separator).map(unquote));

  // Kopfzeile: Die erste Spalte ist keine Nummer.
  if (rows.length > 0 && !/^\d+$/.test(rows[0][0] ?? '')) {
    rows.shift();
  }

  return rows.map((cells) => ({
    number: cells[0] ?? '',
    name: cells[1] ?? '',
    type: typeFrom(cells[2] ?? ''),
    taxKey: cells[3] ? cells[3] : null,
  }));
}

function unquote(cell: string): string {
  const trimmed = cell.trim();
  return trimmed.startsWith('"') && trimmed.endsWith('"')
    ? trimmed.slice(1, -1).replace(/""/g, '"').trim()
    : trimmed;
}

/** Die Art aus dem, was in der Datei steht -- Wort oder Zahl. */
function typeFrom(value: string): number | null {
  const word = value.trim().toLowerCase();
  if (!word) {
    return null;
  }
  const number = Number(word);
  if (Number.isInteger(number) && number >= 1 && number <= 7) {
    return number;
  }
  const words: [RegExp, number][] = [
    [/^(aktiv|bestand|vermögen|anlage)/, AccountType.Asset],
    [/^(passiv|verbindlich)/, AccountType.Liability],
    [/^(eigenkapital|kapital)/, AccountType.Equity],
    [/^(erlös|erloes|ertrag|einnahme)/, AccountType.Revenue],
    [/^(aufwand|kosten|ausgabe)/, AccountType.Expense],
    [/^debitor/, AccountType.Receivable],
    [/^kreditor/, AccountType.Payable],
  ];
  return words.find(([pattern]) => pattern.test(word))?.[1] ?? null;
}
