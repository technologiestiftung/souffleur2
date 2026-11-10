import { CurrencyPipe, DatePipe } from '@angular/common';
import { DestroyRef, Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { verfolgeAdresse } from '../../shared/sprungziel';
import { JournalEntry } from '../../core/api';
import { JournalService } from '../../core/journal.service';
import { Person, PeopleService } from '../../core/people.service';
import { Production, ProductionsService } from '../../core/productions.service';
import {
  IntegrityProblem,
  Voucher,
  VoucherDirection,
  VoucherSource,
  VouchersService,
} from '../../core/vouchers.service';
import { DialogComponent } from '../../shared/dialog.component';
import { DropZoneComponent } from '../../shared/drop-zone.component';
import { ActionsRegistry } from '../../shell/actions.registry';
import { messagesFrom } from '../calendar/event-dialog.component';
import { MeldungComponent } from '../../shared/meldung.component';

/**
 * Die Belege des Hauses.
 *
 * Kein Löschen: Die Datei bleibt acht Jahre, wie sie kam (§ 147 Abs. 3 AO).
 * Was es gibt, ist Anlegen, Lesen, Richtigstellen bis zur Festschreibung,
 * mit einer Buchung verbinden -- und nachrechnen, ob die abgelegten Dateien
 * noch zu ihren Prüfwerten passen.
 */
@Component({
  selector: 'souffleur-vouchers',
  imports: [
    FormsModule,
    CurrencyPipe,
    DatePipe,
    DialogComponent,
    DropZoneComponent,
    MeldungComponent,
  ],
  templateUrl: './vouchers.component.html',
  host: { class: 'block h-full min-h-0' },
})
export class VouchersComponent implements OnInit {
  private readonly vouchers = inject(VouchersService);
  private readonly route = inject(ActivatedRoute);
  private readonly destroyRef = inject(DestroyRef);
  private readonly peopleList = inject(PeopleService);
  private readonly productionList = inject(ProductionsService);
  private readonly actions = inject(ActionsRegistry);
  private readonly journal = inject(JournalService);

  protected readonly loading = this.vouchers.loading;
  protected readonly list = this.vouchers.vouchers;

  protected readonly contacts = signal<Person[]>([]);
  protected readonly productions = signal<Production[]>([]);
  protected readonly entries = signal<JournalEntry[]>([]);

  protected readonly year = signal<number | null>(new Date().getFullYear());
  protected readonly unbookedOnly = signal(false);

  protected readonly problems = signal<string[]>([]);
  protected readonly checked = signal<IntegrityProblem[] | null>(null);
  protected readonly busy = signal(false);

  protected readonly uploading = signal(false);
  protected readonly draft = signal<Record<string, string>>({});
  protected readonly file = signal<File | null>(null);

  /** Welcher Beleg gerade mit einer Buchung verbunden wird. */
  protected readonly linking = signal<string | null>(null);

  /** Welcher Beleg gerade richtiggestellt wird -- null beim Anlegen. */
  protected readonly editing = signal<Voucher | null>(null);

  protected readonly directions = [
    { value: VoucherDirection.Incoming, label: 'Eingang — das Haus zahlt' },
    { value: VoucherDirection.Outgoing, label: 'Ausgang — das Haus bekommt' },
  ];

  protected readonly sources = [
    { value: VoucherSource.Upload, label: 'Hochgeladen' },
    { value: VoucherSource.Photo, label: 'Abfotografiert' },
    { value: VoucherSource.ElectronicInvoice, label: 'E-Rechnung' },
    { value: VoucherSource.Generated, label: 'Hier erstellt' },
    { value: VoucherSource.PaperOnly, label: 'Nur auf Papier' },
  ];

  protected sourceLabel(value: number): string {
    return this.sources.find((s) => s.value === value)?.label ?? '—';
  }

  protected readonly heading = computed(() => {
    const current = this.editing();
    return current ? `Beleg ${current.label} richtigstellen` : 'Beleg hinzufügen';
  });

  protected readonly summary = computed(() => {
    const all = this.list();
    return {
      total: all.length,
      unbooked: all.filter((v) => !v.isBooked).length,
      sum: all.reduce((carry, v) => carry + v.grossAmount, 0),
    };
  });

  async ngOnInit(): Promise<void> {
    this.actions.set(
      'Finanzen',
      [{ label: 'Beleg hinzufügen', icon: 'beleg', primary: true, run: () => this.startNew() }],
      [
        { label: 'Buchungen', icon: 'buchung', path: '/finanzen' },
        { label: 'Bank', icon: 'bank', path: '/finanzen/bank' },
      ],
    );

    // Gleichzeitig, nicht nacheinander: reload() setzt das Ergebnis des
    // Prüflaufs zurück. Lief es erst nach Kontakten und Produktionen, wischte
    // es ein Ergebnis weg, das jemand mit schnellem Klick schon geholt hatte.
    const [contacts, productions] = await Promise.all([
      this.peopleList.all(),
      this.productionList.choices(),
      this.reload(),
    ]);
    this.contacts.set(contacts);
    this.productions.set(productions);

    // Wer von der Übersicht kommt, will den Bogen schon offen sehen.
    if (this.route.snapshot.queryParamMap.get('neu')) {
      this.startNew();
    }

    // Aus der Suche: `?beleg=<id>&jahr=<Jahr>` -- erst das Jahr einstellen,
    // sonst steht der Beleg nicht in der Liste, dann den Bogen öffnen.
    verfolgeAdresse(this.route, this.destroyRef, async (params) => {
      const id = params.get('beleg');
      if (!id) {
        return;
      }
      const jahr = Number(params.get('jahr'));
      if (jahr && jahr !== this.year()) {
        this.year.set(jahr);
        await this.reload();
      }
      const voucher = this.vouchers.vouchers().find((v) => v.id === id);
      if (voucher) {
        this.startEdit(voucher);
      }
    });
  }

  protected async reload(): Promise<void> {
    this.checked.set(null);
    await this.vouchers.load(this.year(), this.unbookedOnly());
  }

  protected startNew(): void {
    this.problems.set([]);
    this.uploading.set(true);
    this.editing.set(null);
    this.file.set(null);

    const today = new Date().toISOString().slice(0, 10);
    this.draft.set({
      date: today,
      fiscalYear: String(Number(today.slice(0, 4))),
      direction: String(VoucherDirection.Incoming),
      source: String(VoucherSource.Upload),
      grossAmount: '',
      description: '',
    });
  }

  /**
   * Denselben Bogen noch einmal, diesmal mit dem, was schon dasteht. Datei
   * und Herkunft fehlen darin: Die ändern sich nicht mehr.
   */
  protected startEdit(voucher: Voucher): void {
    this.problems.set([]);
    this.uploading.set(true);
    this.editing.set(voucher);
    this.file.set(null);

    this.draft.set({
      date: voucher.date.slice(0, 10),
      fiscalYear: String(voucher.fiscalYear),
      direction: String(voucher.direction),
      source: String(voucher.source),
      grossAmount: voucher.grossAmount.toFixed(2),
      description: voucher.description,
      externalNumber: voucher.externalNumber ?? '',
      dueDate: voucher.dueDate?.slice(0, 10) ?? '',
      contactId: voucher.contactId ?? '',
      productionId: voucher.productionId ?? '',
    });
  }

  protected cancel(): void {
    this.uploading.set(false);
    this.editing.set(null);
    this.file.set(null);
  }

  protected patch(change: Record<string, string>): void {
    this.draft.update((current) => {
      const next = { ...current, ...change };

      // Das Geschäftsjahr folgt dem Belegdatum -- der Server besteht darauf,
      // und zweimal dasselbe einzutippen ist eine Fehlerquelle.
      if (change['date']) {
        next['fiscalYear'] = change['date'].slice(0, 4);
      }

      return next;
    });
  }

  protected async save(): Promise<void> {
    const draft = this.draft();

    if (!draft['description']?.trim() || !draft['grossAmount']) {
      this.problems.set(['Betrag und Beschreibung sind nötig.']);
      return;
    }

    // Eine abgewiesene Datei hinterlässt eine leere Fläche. Ohne diese Prüfung
    // entstünde daraus stillschweigend ein Beleg ohne Beleg.
    if (!this.editing() && !this.file() && Number(draft['source']) !== VoucherSource.PaperOnly) {
      this.problems.set([
        'Es ist keine Datei gewählt. Ohne Datei geht es nur mit der Herkunft „Nur auf Papier".',
      ]);
      return;
    }

    this.busy.set(true);
    this.problems.set([]);

    try {
      const existing = this.editing();

      if (existing) {
        await this.vouchers.update(existing.id, {
          date: draft['date'],
          grossAmount: Number(draft['grossAmount']),
          description: draft['description'].trim(),
          direction: Number(draft['direction']),
          externalNumber: draft['externalNumber']?.trim() || null,
          dueDate: draft['dueDate'] || null,
          contactId: draft['contactId'] || null,
          productionId: draft['productionId'] || null,
        });
      } else {
        await this.vouchers.upload(draft, this.file());
      }

      this.cancel();
      await this.reload();
    } catch (error) {
      this.problems.set(messagesFrom(error));
    } finally {
      this.busy.set(false);
    }
  }

  /** Die Buchungen, mit denen sich ein Beleg verbinden lässt. */
  protected async startLink(voucher: Voucher): Promise<void> {
    this.problems.set([]);
    this.linking.set(voucher.id);

    if (this.entries().length === 0) {
      try {
        this.entries.set(await this.journal.entries(200));
      } catch {
        this.problems.set(['Die Buchungen ließen sich nicht laden.']);
      }
    }
  }

  protected async link(voucher: Voucher, journalEntryId: string): Promise<void> {
    if (!journalEntryId) {
      return;
    }

    this.problems.set([]);

    try {
      await this.vouchers.link(voucher.id, journalEntryId);
      this.linking.set(null);
      await this.reload();
    } catch (error) {
      this.problems.set(messagesFrom(error));
    }
  }

  /** Nachrechnen, ob die abgelegten Dateien noch zu ihren Prüfwerten passen. */
  protected async check(): Promise<void> {
    this.busy.set(true);
    this.problems.set([]);

    try {
      this.checked.set(await this.vouchers.integrity(this.year()));
    } catch (error) {
      this.problems.set(messagesFrom(error));
    } finally {
      this.busy.set(false);
    }
  }

  protected fileUrl(voucher: Voucher): string {
    return this.vouchers.fileUrl(voucher);
  }

  protected contactName(id: string | null): string {
    return id ? (this.contacts().find((c) => c.id === id)?.name ?? '—') : '—';
  }

  protected entryLabel(entry: JournalEntry): string {
    return (
      `${entry.fiscalYear}-${String(entry.number).padStart(4, '0')} · ` +
      `${entry.date} · ${entry.description}`
    );
  }

  protected size(bytes: number): string {
    return bytes < 1024 * 1024
      ? `${Math.round(bytes / 1024)} kB`
      : `${(bytes / 1024 / 1024).toFixed(1)} MB`;
  }
}
