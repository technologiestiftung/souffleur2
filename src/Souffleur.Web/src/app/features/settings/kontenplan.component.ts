import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { Account, CostCenter } from '../../core/api';
import { AuthService } from '../../core/auth.service';
import { HouseService } from '../../core/house.service';
import { TAX_SPHERES } from '../../core/journal.service';
import {
  ACCOUNT_TYPES,
  CsvRow,
  ImportResult,
  KontenplanService,
  parseKontenCsv,
} from '../../core/kontenplan.service';
import { DialogComponent } from '../../shared/dialog.component';
import { MeldungComponent } from '../../shared/meldung.component';
import { ActionsRegistry } from '../../shell/actions.registry';
import { messagesFrom } from '../calendar/event-dialog.component';
import { einstellungsBereiche } from './bereiche';

interface Draft {
  number: string;
  name: string;
  type: number | null;
  taxKey: string;
  defaultTaxSphere: number | null;
  isActive: boolean;
}

/** Wie eine Zeile der Datei ausgeht, wenn man jetzt einliest. */
type Vorschau = CsvRow & { lage: 'neu' | 'vorhanden' | 'unbrauchbar' };

/**
 * Kontenplan und Kostenstellen.
 *
 * souffleur liefert keine Konten mit: Welche gelten, sagt das Steuerbüro.
 * Meist kommt die Liste als Datei, deshalb steht das Einlesen gleichrangig
 * neben dem Anlegen einzelner Konten. Gelöscht wird nichts -- alte Buchungen
 * zeigen auf ihr Konto; wer es nicht mehr braucht, legt es still.
 */
@Component({
  selector: 'souffleur-kontenplan',
  imports: [FormsModule, RouterLink, DialogComponent, MeldungComponent],
  templateUrl: './kontenplan.component.html',
})
export class KontenplanComponent implements OnInit {
  private readonly kontenplan = inject(KontenplanService);
  private readonly house = inject(HouseService);
  private readonly auth = inject(AuthService);
  private readonly actions = inject(ActionsRegistry);

  protected readonly types = ACCOUNT_TYPES;
  protected readonly spheres = TAX_SPHERES;

  protected readonly accounts = signal<Account[]>([]);
  protected readonly costCenters = signal<CostCenter[]>([]);
  protected readonly chartLabel = signal('');
  protected readonly needsSpheres = signal(false);

  /** null: noch nicht geladen; false: Finanzen aus oder kein Recht. */
  protected readonly available = signal<boolean | null>(null);
  protected readonly canBook = computed(() => this.auth.can('FinanceBook'));

  protected readonly filter = signal('');
  protected readonly showInactive = signal(false);
  protected readonly shown = computed(() => {
    const words = this.filter().trim().toLowerCase().split(/\s+/).filter(Boolean);
    return this.accounts().filter(
      (a) =>
        (this.showInactive() || a.isActive) &&
        words.every((w) => `${a.number} ${a.name}`.toLowerCase().includes(w)),
    );
  });
  protected readonly inactiveCount = computed(
    () => this.accounts().filter((a) => !a.isActive).length,
  );

  protected readonly problems = signal<string[]>([]);
  protected readonly busy = signal(false);

  /** 'neu' oder die Id des Kontos im Bogen. */
  protected readonly editing = signal<string | null>(null);
  protected readonly draft = signal<Draft>(emptyDraft());

  /** Die gelesene Datei, bis jemand „Einlesen" drückt. */
  protected readonly vorschau = signal<Vorschau[] | null>(null);
  protected readonly dateiname = signal('');
  protected readonly ergebnis = signal<ImportResult | null>(null);

  protected readonly einzulesen = computed(
    () => this.vorschau()?.filter((r) => r.lage === 'neu') ?? [],
  );
  protected readonly ohneArt = computed(() => this.einzulesen().filter((r) => r.type === null));

  protected readonly costNumber = signal('');
  protected readonly costName = signal('');
  protected readonly costProblems = signal<string[]>([]);

  async ngOnInit(): Promise<void> {
    this.actions.set(
      'Einstellungen',
      this.canBook()
        ? [{ label: 'Konto anlegen', icon: 'buchung', primary: true, run: () => this.startNew() }]
        : [],
      einstellungsBereiche(this.auth.can('AccountsManage')),
    );

    try {
      const [accounts, costCenters, organization] = await Promise.all([
        this.kontenplan.accounts(),
        this.kontenplan.costCenters(),
        this.house.organization(),
      ]);
      this.accounts.set(accounts);
      this.costCenters.set(costCenters);
      this.chartLabel.set(organization.chartLabel);
      this.needsSpheres.set(organization.requiresTaxSpheres);
      this.available.set(true);
    } catch {
      this.available.set(false);
    }
  }

  protected typeLabel(type: number | null): string {
    return this.types.find((t) => t.value === type)?.label ?? '—';
  }

  protected sphereLabel(sphere: number | null): string {
    return this.spheres.find((s) => s.value === sphere)?.label ?? '—';
  }

  protected startNew(): void {
    this.problems.set([]);
    this.editing.set('neu');
    this.draft.set(emptyDraft());
  }

  protected startEdit(account: Account): void {
    this.problems.set([]);
    this.editing.set(account.id);
    this.draft.set({
      number: account.number,
      name: account.name,
      type: account.type,
      taxKey: account.taxKey ?? '',
      defaultTaxSphere: account.defaultTaxSphere,
      isActive: account.isActive,
    });
  }

  protected patch(change: Partial<Draft>): void {
    this.draft.update((current) => ({ ...current, ...change }));
  }

  protected cancel(): void {
    this.editing.set(null);
  }

  protected async save(): Promise<void> {
    const draft = this.draft();
    if (draft.type === null) {
      this.problems.set(['Die Kontenart ist nötig.']);
      return;
    }

    this.busy.set(true);
    this.problems.set([]);
    try {
      const fields = {
        name: draft.name,
        type: Number(draft.type),
        taxKey: draft.taxKey.trim() || null,
        defaultTaxSphere: draft.defaultTaxSphere === null ? null : Number(draft.defaultTaxSphere),
      };
      if (this.editing() === 'neu') {
        await this.kontenplan.create({ ...fields, number: draft.number });
      } else {
        await this.kontenplan.update(this.editing()!, { ...fields, isActive: draft.isActive });
      }
      this.editing.set(null);
      await this.reload();
    } catch (error) {
      this.problems.set(problemsOf(error));
    } finally {
      this.busy.set(false);
    }
  }

  /** Stilllegen oder wieder aufnehmen, ohne den Bogen. */
  protected async setActive(account: Account, isActive: boolean): Promise<void> {
    this.problems.set([]);
    try {
      await this.kontenplan.update(account.id, {
        name: account.name,
        type: account.type,
        taxKey: account.taxKey,
        defaultTaxSphere: account.defaultTaxSphere,
        isActive,
      });
      await this.reload();
    } catch (error) {
      this.problems.set(problemsOf(error));
    }
  }

  /** Die Datei lesen und zeigen, was passieren würde -- noch nichts anlegen. */
  protected async dateiGewaehlt(event: Event): Promise<void> {
    const field = event.target as HTMLInputElement;
    const file = field.files?.[0];
    field.value = '';
    if (!file) {
      return;
    }

    const rows = parseKontenCsv(await file.text());
    const known = new Set(this.accounts().map((a) => a.number));
    this.dateiname.set(file.name);
    this.ergebnis.set(null);
    this.vorschau.set(
      rows.map((row) => ({
        ...row,
        lage: !/^\d+$/.test(row.number) || !row.name ? 'unbrauchbar' : known.has(row.number) ? 'vorhanden' : 'neu',
      })),
    );
  }

  protected setRowType(row: Vorschau, type: number): void {
    this.vorschau.update((rows) =>
      rows?.map((r) => (r === row ? { ...r, type: Number(type) } : r)) ?? null,
    );
  }

  /** Allen Zeilen ohne Art dieselbe geben -- eine Datei nur mit Aufwandskonten ist häufig. */
  protected setMissingTypes(type: number): void {
    if (!type) {
      return;
    }
    this.vorschau.update(
      (rows) => rows?.map((r) => (r.type === null ? { ...r, type: Number(type) } : r)) ?? null,
    );
  }

  protected verwerfen(): void {
    this.vorschau.set(null);
  }

  protected async einlesen(): Promise<void> {
    const rows = this.einzulesen();
    if (rows.length === 0 || this.ohneArt().length > 0) {
      return;
    }

    this.busy.set(true);
    try {
      this.ergebnis.set(
        await this.kontenplan.import(
          rows.map((r) => ({
            number: r.number,
            name: r.name,
            type: r.type!,
            taxKey: r.taxKey,
            defaultTaxSphere: null,
          })),
        ),
      );
      this.vorschau.set(null);
      await this.reload();
    } catch (error) {
      this.problems.set(problemsOf(error));
    } finally {
      this.busy.set(false);
    }
  }

  protected async addCostCenter(): Promise<void> {
    this.costProblems.set([]);
    const number = this.costNumber().trim();
    const name = this.costName().trim();
    if (!number || !name) {
      this.costProblems.set(['Nummer und Name sind nötig.']);
      return;
    }
    try {
      await this.kontenplan.createCostCenter(number, name);
      this.costNumber.set('');
      this.costName.set('');
      this.costCenters.set(await this.kontenplan.costCenters());
    } catch (error) {
      this.costProblems.set(problemsOf(error));
    }
  }

  private async reload(): Promise<void> {
    this.accounts.set(await this.kontenplan.accounts());
  }
}

function emptyDraft(): Draft {
  return { number: '', name: '', type: null, taxKey: '', defaultTaxSphere: null, isActive: true };
}

/** 409 kommt hier als Satz, nicht als ValidationProblem. */
function problemsOf(error: unknown): string[] {
  const answer = error as { status?: number; error?: unknown };
  if (answer?.status === 409 && typeof answer.error === 'string') {
    return [answer.error];
  }
  return messagesFrom(error);
}
