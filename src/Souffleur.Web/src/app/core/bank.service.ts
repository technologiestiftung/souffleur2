import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';

export interface BankAccount {
  id: string;
  name: string;
  iban: string;
  ibanGrouped: string;
  bic: string | null;
  bankName: string | null;
  accountId: string | null;
  accountLabel: string | null;
  balance: number | null;
  balanceDate: string | null;
  isActive: boolean;
}

export interface BankTransaction {
  id: string;
  bankAccountId: string;
  bookedOn: string;
  valueDate: string | null;
  amount: number;
  currency: string;
  purpose: string | null;
  counterpartyName: string | null;
  counterpartyIban: string | null;
  endToEndId: string | null;
  voucherId: string | null;
  voucherLabel: string | null;
  journalEntryId: string | null;
  isAssigned: boolean;
}

/** Ein Beleg, der zu einem Umsatz passen könnte. */
export interface MatchSuggestion {
  voucherId: string;
  label: string;
  description: string;
  grossAmount: number;
  date: string;
  score: number;
  reason: string;
}

export interface ImportResult {
  added: number;
  alreadyKnown: number;
  balance: number | null;
  balanceDate: string | null;
}

/**
 * Konten, Auszüge und der Abgleich.
 *
 * Noch ohne Verbindung zur Bank: Der Auszug kommt als Datei. FinTS braucht
 * den ARPA-Stand von libfintx, ein echtes Konto und ein TAN-Gerät — siehe
 * `docs/bankanbindung-uebernahme-aus-arpa.md`.
 */
@Injectable({ providedIn: 'root' })
export class BankService {
  private readonly http = inject(HttpClient);

  private readonly accountList = signal<BankAccount[]>([]);
  private readonly transactionList = signal<BankTransaction[]>([]);
  private readonly busy = signal(false);

  readonly accounts = this.accountList.asReadonly();
  readonly transactions = this.transactionList.asReadonly();
  readonly loading = this.busy.asReadonly();

  async loadAccounts(): Promise<BankAccount[]> {
    try {
      const found = await firstValueFrom(
        this.http.get<BankAccount[]>('/api/banking/accounts'),
      );
      this.accountList.set(found);
      return found;
    } catch {
      this.accountList.set([]);
      return [];
    }
  }

  async loadTransactions(accountId?: string | null, unassignedOnly = false): Promise<void> {
    this.busy.set(true);

    try {
      let params = new HttpParams();
      if (accountId) {
        params = params.set('account', accountId);
      }
      if (unassignedOnly) {
        params = params.set('unassignedOnly', true);
      }

      this.transactionList.set(
        await firstValueFrom(
          this.http.get<BankTransaction[]>('/api/banking/transactions', { params }),
        ),
      );
    } catch {
      this.transactionList.set([]);
    } finally {
      this.busy.set(false);
    }
  }

  /** Alle Umsätze, ohne die Liste der Bankseite anzufassen. */
  allTransactions(): Promise<BankTransaction[]> {
    return firstValueFrom(this.http.get<BankTransaction[]>('/api/banking/transactions'));
  }

  createAccount(input: Partial<BankAccount>): Promise<BankAccount> {
    return firstValueFrom(this.http.post<BankAccount>('/api/banking/accounts', input));
  }

  async removeAccount(id: string): Promise<void> {
    await firstValueFrom(this.http.delete(`/api/banking/accounts/${id}`));
  }

  /** Einen Auszug einlesen. Schon bekannte Umsätze übergeht der Server. */
  importStatement(accountId: string, file: File): Promise<ImportResult> {
    const form = new FormData();
    form.append('file', file, file.name);

    return firstValueFrom(
      this.http.post<ImportResult>(`/api/banking/accounts/${accountId}/statements`, form),
    );
  }

  suggestions(transactionId: string): Promise<MatchSuggestion[]> {
    return firstValueFrom(
      this.http.get<MatchSuggestion[]>(
        `/api/banking/transactions/${transactionId}/suggestions`,
      ),
    );
  }

  async assign(transactionId: string, voucherId: string | null): Promise<void> {
    await firstValueFrom(
      this.http.post(`/api/banking/transactions/${transactionId}/assign`, { voucherId }),
    );
  }
}
