import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';

export interface VoucherFile {
  originalName: string;
  contentType: string;
  sizeInBytes: number;
  sha256: string;
}

export interface Voucher {
  id: string;
  label: string;
  number: number;
  fiscalYear: number;
  date: string;
  grossAmount: number;
  description: string;
  externalNumber: string | null;
  dueDate: string | null;
  contactId: string | null;
  productionId: string | null;
  direction: number;
  source: number;
  isBooked: boolean;
  isCommitted: boolean;
  journalEntryId: string | null;
  file: VoucherFile | null;
}

/** Was sich an einem Beleg nachtragen oder richtigstellen lässt. */
export interface VoucherChanges {
  date: string;
  grossAmount: number;
  description: string;
  direction: number;
  externalNumber: string | null;
  dueDate: string | null;
  contactId: string | null;
  productionId: string | null;
}

/** Ein Beleg, der sich nicht mit seinem Prüfwert deckt. */
export interface IntegrityProblem {
  voucherId: string;
  label: string;
  problem: string;
}

export const VoucherDirection = { Incoming: 1, Outgoing: 2 } as const;

/**
 * Die Werte müssen zu VoucherSource im Domänenmodell passen -- hier stand
 * einmal 4 für "Per E-Mail", während der Server darunter "in souffleur
 * erstellt" versteht. Die Herkunft wurde damit falsch festgehalten, und
 * gerade sie soll nachvollziehbar bleiben.
 */
export const VoucherSource = {
  Upload: 1,
  Photo: 2,
  ElectronicInvoice: 3,
  Generated: 4,
  PaperOnly: 5,
} as const;

/**
 * Die Belege des Hauses.
 *
 * Die Datei wird hochgeladen und nie wieder verändert (§ 147 Abs. 3 AO).
 * Die Angaben darum herum lassen sich richtigstellen, solange der Beleg ein
 * Entwurf ist -- gelöscht wird nie.
 */
@Injectable({ providedIn: 'root' })
export class VouchersService {
  private readonly http = inject(HttpClient);

  private readonly loaded = signal<Voucher[]>([]);
  private readonly busy = signal(false);

  readonly vouchers = this.loaded.asReadonly();
  readonly loading = this.busy.asReadonly();

  async load(fiscalYear?: number | null, unbookedOnly = false): Promise<Voucher[]> {
    this.busy.set(true);

    try {
      let params = new HttpParams().set('take', 500);
      if (fiscalYear) {
        params = params.set('fiscalYear', fiscalYear);
      }
      if (unbookedOnly) {
        params = params.set('unbookedOnly', true);
      }

      const found = await firstValueFrom(
        this.http.get<Voucher[]>('/api/accounting/vouchers', { params }),
      );
      this.loaded.set(found);
      return found;
    } catch {
      // Modul aus oder kein Recht: dann gibt es nichts zu zeigen.
      this.loaded.set([]);
      return [];
    } finally {
      this.busy.set(false);
    }
  }

  /**
   * Was noch auf eine Buchung wartet. Lässt die Liste oben unberührt -- die
   * gehört der Belegseite mit ihren Filtern.
   */
  unbooked(take: number): Promise<Voucher[]> {
    const params = new HttpParams().set('unbookedOnly', true).set('take', take);
    return firstValueFrom(this.http.get<Voucher[]>('/api/accounting/vouchers', { params }));
  }

  /** Alle Belege, ohne die Liste oben anzufassen -- für die Kennzahlen. */
  all(take = 500): Promise<Voucher[]> {
    const params = new HttpParams().set('take', take);
    return firstValueFrom(this.http.get<Voucher[]>('/api/accounting/vouchers', { params }));
  }

  /**
   * Hochladen als Formular: Die Datei geht mit den Angaben zusammen hin,
   * damit der Server beides in einem Zug prüfen und ablegen kann.
   */
  upload(fields: Record<string, string>, file: File | null): Promise<Voucher> {
    const form = new FormData();

    for (const [name, value] of Object.entries(fields)) {
      if (value !== '') {
        form.append(name, value);
      }
    }

    if (file) {
      form.append('file', file, file.name);
    }

    return firstValueFrom(this.http.post<Voucher>('/api/accounting/vouchers', form));
  }

  /** Die Adresse der hinterlegten Datei -- der Browser lädt sie selbst. */
  fileUrl(voucher: Voucher): string {
    return `/api/accounting/vouchers/${voucher.id}/file`;
  }

  /**
   * Stellt die Angaben richtig. Als JSON und ohne Datei: Die bleibt, wie sie
   * kam, und ein Formular mit Dateifeld würde das Gegenteil nahelegen.
   */
  update(voucherId: string, changes: VoucherChanges): Promise<Voucher> {
    return firstValueFrom(
      this.http.put<Voucher>(`/api/accounting/vouchers/${voucherId}`, changes),
    );
  }

  async link(voucherId: string, journalEntryId: string): Promise<void> {
    await firstValueFrom(
      this.http.post(`/api/accounting/vouchers/${voucherId}/link/${journalEntryId}`, {}),
    );
  }

  /** Prüfwerte nachrechnen: Fehlt eine Datei, hat sich eine geändert? */
  integrity(fiscalYear?: number | null): Promise<IntegrityProblem[]> {
    const params = fiscalYear
      ? new HttpParams().set('fiscalYear', fiscalYear)
      : new HttpParams();

    return firstValueFrom(
      this.http.get<IntegrityProblem[]>('/api/accounting/vouchers/integrity', { params }),
    );
  }
}
