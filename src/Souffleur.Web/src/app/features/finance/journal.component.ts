import { CurrencyPipe, DatePipe } from '@angular/common';
import { DestroyRef, Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { verfolgeAdresse } from '../../shared/sprungziel';
import { Account, CostCenter, JournalEntry, OrganizationInfo } from '../../core/api';
import { HouseService } from '../../core/house.service';
import {
  DatevPreview,
  JournalEntryInput,
  JournalService,
  TAX_SPHERES,
} from '../../core/journal.service';
import { DialogComponent } from '../../shared/dialog.component';
import { MetricStripComponent } from '../../shared/metric-strip.component';
import { ActionsRegistry } from '../../shell/actions.registry';
import { messagesFrom } from '../calendar/event-dialog.component';
import { MeldungComponent } from '../../shared/meldung.component';

/** Eine Zeile im Bogen. Alles Text, bis sie zum Server geht. */
interface DraftLine {
  accountId: string;
  debit: string;
  credit: string;
  costCenterId: string;
  taxSphere: string;
  description: string;
}

function emptyLine(): DraftLine {
  return { accountId: '', debit: '', credit: '', costCenterId: '', taxSphere: '', description: '' };
}

function amount(value: string): number {
  // Im Formular steht, was jemand tippt -- auch ein Komma.
  const parsed = Number(value.replace(',', '.'));
  return Number.isFinite(parsed) ? parsed : 0;
}

@Component({
  selector: 'souffleur-journal',
  imports: [
    CurrencyPipe,
    DatePipe,
    FormsModule,
    MetricStripComponent,
    DialogComponent,
    MeldungComponent,
  ],
  templateUrl: './journal.component.html',
})
export class JournalComponent implements OnInit {
  private readonly journal = inject(JournalService);
  private readonly house = inject(HouseService);
  private readonly actions = inject(ActionsRegistry);
  private readonly route = inject(ActivatedRoute);
  private readonly destroyRef = inject(DestroyRef);

  /** Die Buchung, zu der ein Sprung aus der Suche führte -- bleibt markiert, bis man weiterklickt. */
  protected readonly hervorgehoben = signal<string | null>(null);
  private readonly router = inject(Router);

  protected readonly entries = signal<JournalEntry[]>([]);
  protected readonly accounts = signal<Account[]>([]);
  protected readonly costCenters = signal<CostCenter[]>([]);
  protected readonly organization = signal<OrganizationInfo | null>(null);
  protected readonly loading = signal(true);
  protected readonly blocked = signal(false);

  /** Der Buchungsbogen, solange er offen ist. */
  protected readonly booking = signal(false);
  protected readonly date = signal('');
  protected readonly description = signal('');
  protected readonly documentNumber = signal('');
  protected readonly lines = signal<DraftLine[]>([]);
  protected readonly problems = signal<string[]>([]);
  protected readonly busy = signal(false);

  /** Der DATEV-Stapel, solange jemand ihn einrichtet. */
  protected readonly datev = signal(false);
  protected readonly datevFrom = signal('');
  protected readonly datevTo = signal('');
  protected readonly preview = signal<DatevPreview | null>(null);

  protected readonly spheres = TAX_SPHERES;

  protected readonly debitTotal = computed(() =>
    this.entries().reduce((sum, e) => sum + e.total, 0),
  );

  protected readonly drafts = computed(() => this.entries().filter((e) => !e.isCommitted).length);

  /** Soll und Haben des Bogens, laufend -- die Buchung muss aufgehen. */
  protected readonly draftTotals = computed(() => {
    const lines = this.lines();
    return {
      debit: lines.reduce((sum, line) => sum + amount(line.debit), 0),
      credit: lines.reduce((sum, line) => sum + amount(line.credit), 0),
    };
  });

  protected readonly balanced = computed(() => {
    const { debit, credit } = this.draftTotals();
    return debit > 0 && Math.abs(debit - credit) < 0.005;
  });

  /** Nur gemeinnützige Häuser mit Sphärentrennung sehen die Spalte. */
  protected readonly needsSpheres = computed(
    () => this.organization()?.requiresTaxSpheres ?? false,
  );

  /** Konten für die Anzeige, nach Id nachschlagbar. */
  private readonly accountsById = computed(() => new Map(this.accounts().map((a) => [a.id, a])));

  private readonly costCentersById = computed(
    () => new Map(this.costCenters().map((c) => [c.id, c])),
  );

  async ngOnInit(): Promise<void> {
    this.actions.set(
      'Finanzen',
      [
        {
          label: 'Buchung erfassen',
          icon: 'plus',
          primary: true,
          run: () => this.startBooking(),
        },
        { label: 'Beleg hochladen', icon: 'beleg', run: () => this.goToVouchers() },
        { label: 'DATEV-Stapel', icon: 'download', run: () => this.startDatev() },
      ],
      [
        { label: 'Buchen', path: '/finanzen' },
        { label: 'Belege', icon: 'beleg', path: '/finanzen/belege' },
        { label: 'Bank', icon: 'bank', path: '/finanzen/bank' },
        { label: 'Budget', path: '/finanzen/budget' },
        { label: 'Inventar', path: '/finanzen/inventar' },
      ],
    );

    try {
      const [entries, accounts, costCenters, organization] = await Promise.all([
        this.journal.entries(100),
        this.journal.accounts(),
        this.journal.costCenters(),
        this.house.organization(),
      ]);
      this.entries.set(entries);
      this.accounts.set(accounts);
      this.costCenters.set(costCenters);
      this.organization.set(organization);
    } catch {
      // 404 heißt hier: Das Modul Finanzen ist in diesem Haus abgeschaltet.
      this.blocked.set(true);
    } finally {
      this.loading.set(false);
    }

    if (this.route.snapshot.queryParamMap.get('neu')) {
      this.startBooking();
    }

    // Aus der Suche: `?buchung=<id>` hebt die Zeile hervor und rollt hin.
    verfolgeAdresse(this.route, this.destroyRef, (params) => {
      const id = params.get('buchung');
      if (!id) {
        return;
      }
      this.hervorgehoben.set(id);
      setTimeout(() =>
        document.getElementById('buchung-' + id)?.scrollIntoView({ block: 'center' }),
      );
    });
  }

  protected accountLabel(id: string): string {
    return this.accountsById().get(id)?.number ?? '—';
  }

  protected accountName(id: string): string {
    return this.accountsById().get(id)?.name ?? '';
  }

  protected costCenterLabel(id: string | null): string {
    return id ? (this.costCentersById().get(id)?.number ?? '—') : '—';
  }

  /* ---------------------------------------------------------- Buchungsbogen */

  protected startBooking(): void {
    this.problems.set([]);
    this.datev.set(false);
    this.booking.set(true);
    this.date.set(new Date().toISOString().slice(0, 10));
    this.description.set('');
    this.documentNumber.set('');

    // Zwei Zeilen von Anfang an: Unter zweien gibt es keine Buchung.
    this.lines.set([emptyLine(), emptyLine()]);
  }

  protected cancelBooking(): void {
    this.booking.set(false);
    this.problems.set([]);
  }

  protected addLine(): void {
    this.lines.update((current) => [...current, emptyLine()]);
  }

  protected removeLine(index: number): void {
    this.lines.update((current) => current.filter((_, i) => i !== index));
  }

  protected patchLine(index: number, change: Partial<DraftLine>): void {
    this.lines.update((current) =>
      current.map((line, i) => (i === index ? { ...line, ...change } : line)),
    );
  }

  /**
   * Legt die Buchung an. Sie entsteht als Entwurf; festgeschrieben wird
   * später und ausdrücklich, denn danach lässt sie sich nicht mehr ändern.
   */
  protected async saveBooking(): Promise<void> {
    const lines = this.lines().filter((line) => line.accountId);

    const problems: string[] = [];
    if (!this.description().trim()) {
      problems.push('Die Buchung braucht einen Buchungstext.');
    }
    if (lines.length < 2) {
      problems.push('Eine Buchung braucht mindestens zwei Zeilen mit Konto.');
    }
    if (!this.balanced()) {
      problems.push('Soll und Haben stimmen nicht überein.');
    }
    if (this.needsSpheres() && lines.some((line) => !line.taxSphere && this.isResult(line))) {
      problems.push('Erfolgskonten brauchen in diesem Haus eine steuerliche Sphäre.');
    }
    if (problems.length > 0) {
      this.problems.set(problems);
      return;
    }

    const input: JournalEntryInput = {
      fiscalYear: Number(this.date().slice(0, 4)),
      date: this.date(),
      description: this.description().trim(),
      documentNumber: this.documentNumber().trim() || null,
      lines: lines.map((line) => ({
        accountId: line.accountId,
        debit: amount(line.debit),
        credit: amount(line.credit),
        costCenterId: line.costCenterId || null,
        taxSphere: line.taxSphere ? Number(line.taxSphere) : null,
        description: line.description.trim() || null,
      })),
    };

    this.busy.set(true);
    this.problems.set([]);

    try {
      await this.journal.create(input);
      this.entries.set(await this.journal.entries(100));
      this.booking.set(false);
    } catch (error) {
      this.problems.set(messagesFrom(error));
    } finally {
      this.busy.set(false);
    }
  }

  /** Schreibt einen Entwurf fest -- danach ist er unveränderlich. */
  protected async commit(entry: JournalEntry): Promise<void> {
    this.busy.set(true);
    this.problems.set([]);

    try {
      await this.journal.commit(entry.id);
      this.entries.set(await this.journal.entries(100));
    } catch (error) {
      this.problems.set(messagesFrom(error));
    } finally {
      this.busy.set(false);
    }
  }

  /* ------------------------------------------------------------ DATEV */

  protected async startDatev(): Promise<void> {
    this.problems.set([]);
    this.booking.set(false);
    this.datev.set(true);

    const today = new Date();
    this.datevFrom.set(`${today.getFullYear()}-01-01`);
    this.datevTo.set(today.toISOString().slice(0, 10));
    await this.loadPreview();
  }

  protected async loadPreview(): Promise<void> {
    this.preview.set(null);
    try {
      this.preview.set(await this.journal.datevPreview(this.datevFrom(), this.datevTo()));
    } catch (error) {
      this.problems.set(messagesFrom(error));
    }
  }

  protected async exportDatev(): Promise<void> {
    this.busy.set(true);
    this.problems.set([]);

    try {
      const { file, name } = await this.journal.datevExport(this.datevFrom(), this.datevTo());

      // Der Browser legt die Datei ab; der Server hat sie nur geliefert.
      const url = URL.createObjectURL(file);
      const link = document.createElement('a');
      link.href = url;
      link.download = name;
      link.click();
      URL.revokeObjectURL(url);

      this.entries.set(await this.journal.entries(100));
      this.datev.set(false);
    } catch (error) {
      this.problems.set(messagesFrom(error));
    } finally {
      this.busy.set(false);
    }
  }

  protected closeDatev(): void {
    this.datev.set(false);
    this.problems.set([]);
  }

  private goToVouchers(): void {
    void this.router.navigate(['/finanzen/belege'], { queryParams: { neu: 1 } });
  }

  /** Erfolgskonto? Nur Ertrag (4) und Aufwand (5) tragen eine Sphäre. */
  private isResult(line: DraftLine): boolean {
    const type = this.accountsById().get(line.accountId)?.type;
    return type === 4 || type === 5;
  }
}
