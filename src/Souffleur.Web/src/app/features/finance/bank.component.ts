import { CurrencyPipe, DatePipe } from '@angular/common';
import { DestroyRef, Component, OnInit, computed, inject, signal } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { verfolgeAdresse } from '../../shared/sprungziel';
import { FormsModule } from '@angular/forms';
import {
  BankAccount,
  BankService,
  BankTransaction,
  ImportResult,
  MatchSuggestion,
} from '../../core/bank.service';
import { DialogComponent } from '../../shared/dialog.component';
import { DropZoneComponent } from '../../shared/drop-zone.component';
import { ActionsRegistry } from '../../shell/actions.registry';
import { messagesFrom } from '../calendar/event-dialog.component';
import { MeldungComponent } from '../../shared/meldung.component';

/**
 * Konten, Auszüge und der Abgleich.
 *
 * Der Auszug kommt als Datei (CAMT.053). Die Verbindung zur Bank selbst
 * fehlt noch und hängt an Dingen, die es hier nicht gibt: dem ARPA-Stand von
 * libfintx, einem echten Konto und einem TAN-Gerät.
 */
@Component({
  selector: 'souffleur-bank',
  imports: [
    FormsModule,
    CurrencyPipe,
    DatePipe,
    DialogComponent,
    DropZoneComponent,
    MeldungComponent,
  ],
  templateUrl: './bank.component.html',
  host: { class: 'block h-full min-h-0' },
})
export class BankComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly destroyRef = inject(DestroyRef);

  /** Der Umsatz, zu dem ein Sprung aus der Suche führte. */
  protected readonly hervorgehoben = signal<string | null>(null);
  private readonly bank = inject(BankService);
  private readonly actions = inject(ActionsRegistry);

  protected readonly accounts = this.bank.accounts;
  protected readonly transactions = this.bank.transactions;
  protected readonly loading = this.bank.loading;

  protected readonly account = signal<string>('');
  protected readonly unassignedOnly = signal(false);

  protected readonly problems = signal<string[]>([]);
  protected readonly imported = signal<ImportResult | null>(null);
  protected readonly busy = signal(false);

  protected readonly creating = signal(false);

  /** Das Fenster zum Einlesen eines Auszugs, und die Datei, die darin liegt. */
  protected readonly importing = signal(false);
  protected readonly statement = signal<File | null>(null);
  protected readonly draft = signal<Partial<BankAccount>>({});

  /** Für welchen Umsatz gerade Belege gesucht werden. */
  protected readonly matching = signal<string | null>(null);
  protected readonly suggestions = signal<MatchSuggestion[]>([]);

  protected readonly chosen = computed(
    () => this.accounts().find((a) => a.id === this.account()) ?? null,
  );

  protected readonly summary = computed(() => {
    const all = this.transactions();
    return {
      total: all.length,
      open: all.filter((t) => !t.isAssigned).length,
      incoming: all.filter((t) => t.amount > 0).reduce((sum, t) => sum + t.amount, 0),
      outgoing: all.filter((t) => t.amount < 0).reduce((sum, t) => sum + t.amount, 0),
    };
  });

  async ngOnInit(): Promise<void> {
    this.actions.set(
      'Finanzen',
      [{ label: 'Konto anlegen', icon: 'bank', primary: true, run: () => this.startNew() }],
      [
        { label: 'Buchungen', icon: 'buchung', path: '/finanzen' },
        { label: 'Belege', icon: 'beleg', path: '/finanzen/belege' },
      ],
    );

    const found = await this.bank.loadAccounts();
    if (found.length > 0) {
      this.account.set(found[0].id);
    }

    await this.reload();

    // Aus der Suche: `?umsatz=<id>&konto=<id>` -- erst das Konto wählen, dann
    // die Zeile hervorheben und hinrollen.
    verfolgeAdresse(this.route, this.destroyRef, async (params) => {
      const id = params.get('umsatz');
      if (!id) {
        return;
      }
      const konto = params.get('konto');
      if (konto && konto !== this.account()) {
        this.account.set(konto);
        await this.reload();
      }
      this.hervorgehoben.set(id);
      setTimeout(() =>
        document.getElementById('umsatz-' + id)?.scrollIntoView({ block: 'center' }),
      );
    });
  }

  protected async reload(): Promise<void> {
    this.matching.set(null);
    await this.bank.loadTransactions(this.account() || null, this.unassignedOnly());
  }

  protected startNew(): void {
    this.problems.set([]);
    this.creating.set(true);
    this.draft.set({ name: '', iban: '' });
  }

  protected cancel(): void {
    this.creating.set(false);
  }

  protected patch(change: Partial<BankAccount>): void {
    this.draft.update((current) => ({ ...current, ...change }));
  }

  protected async save(): Promise<void> {
    const draft = this.draft();
    this.busy.set(true);
    this.problems.set([]);

    try {
      const created = await this.bank.createAccount({
        name: draft.name?.trim() ?? '',
        iban: draft.iban?.trim() ?? '',
        bic: draft.bic?.trim() || null,
        bankName: draft.bankName?.trim() || null,
      });

      this.creating.set(false);
      await this.bank.loadAccounts();
      this.account.set(created.id);
      await this.reload();
    } catch (error) {
      this.problems.set(messagesFrom(error));
    } finally {
      this.busy.set(false);
    }
  }

  protected startImport(): void {
    this.problems.set([]);
    this.imported.set(null);
    this.statement.set(null);
    this.importing.set(true);
  }

  protected cancelImport(): void {
    this.importing.set(false);
    this.statement.set(null);
  }

  /** Einen Auszug einlesen. Was schon bekannt ist, übergeht der Server. */
  protected async importStatement(): Promise<void> {
    const file = this.statement();
    const account = this.account();

    if (!file || !account) {
      return;
    }

    this.busy.set(true);
    this.problems.set([]);
    this.imported.set(null);

    try {
      this.imported.set(await this.bank.importStatement(account, file));
      this.cancelImport();
      await this.bank.loadAccounts();
      await this.reload();
    } catch (error) {
      this.problems.set(messagesFrom(error));
    } finally {
      this.busy.set(false);
    }
  }

  protected async match(transaction: BankTransaction): Promise<void> {
    this.problems.set([]);
    this.matching.set(transaction.id);
    this.suggestions.set([]);

    try {
      this.suggestions.set(await this.bank.suggestions(transaction.id));
    } catch (error) {
      this.problems.set(messagesFrom(error));
    }
  }

  protected async assign(transaction: BankTransaction, voucherId: string): Promise<void> {
    this.problems.set([]);

    try {
      await this.bank.assign(transaction.id, voucherId);
      this.matching.set(null);
      await this.reload();
    } catch (error) {
      this.problems.set(messagesFrom(error));
    }
  }

  protected async unassign(transaction: BankTransaction): Promise<void> {
    await this.bank.assign(transaction.id, null);
    await this.reload();
  }
}
