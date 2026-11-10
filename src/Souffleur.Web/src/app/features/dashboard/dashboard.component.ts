import { CurrencyPipe, DatePipe } from '@angular/common';
import { Component, ElementRef, OnInit, computed, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { JournalEntry, OrganizationInfo } from '../../core/api';
import { HouseService } from '../../core/house.service';
import { JournalService } from '../../core/journal.service';
import { MetricsService } from '../../core/metrics.service';
import { ModulesService } from '../../core/modules.service';
import { PreferencesService } from '../../core/preferences.service';
import { Voucher, VouchersService } from '../../core/vouchers.service';
import { AnsageService } from '../../shared/ansage.service';
import { IconComponent } from '../../shared/icon.component';
import { MetricStripComponent } from '../../shared/metric-strip.component';
import { SortierbarDirective } from '../../shared/sortierbar.directive';
import { ActionsRegistry } from '../../shell/actions.registry';
import { BlockMenueComponent } from './block-menue.component';

/** Ein Block der Übersicht: die Kennzahlen eines Moduls -- oder die Buchungen. */
interface Block {
  key: string;
  label: string;
  /** Der Pfad des Moduls, dessen Kennzahlen hier stehen; null bei den Buchungen. */
  path: string | null;
}

/**
 * Der eine Block, der kein Modul ist: Was zuletzt gebucht wurde und welche
 * Belege noch auf ihre Buchung warten.
 */
const BUCHUNGEN: Block = { key: 'buchungen', label: 'Buchungen und Belege', path: null };

/** Unter diesen Schlüsseln merkt sich das Konto, wie die Übersicht aussieht. */
const REIHENFOLGE = 'uebersicht';
const AUSGEBLENDET = 'uebersicht:ausgeblendet';

@Component({
  selector: 'souffleur-dashboard',
  imports: [
    CurrencyPipe,
    DatePipe,
    RouterLink,
    MetricStripComponent,
    IconComponent,
    SortierbarDirective,
    BlockMenueComponent,
  ],
  templateUrl: './dashboard.component.html',
  host: {
    '(document:click)': 'aussen($event)',
    '(document:keydown.escape)': 'einrichten.set(false)',
  },
})
export class DashboardComponent implements OnInit {
  private readonly house = inject(HouseService);
  private readonly journal = inject(JournalService);
  private readonly voucherList = inject(VouchersService);
  private readonly actions = inject(ActionsRegistry);
  private readonly router = inject(Router);
  private readonly preferences = inject(PreferencesService);
  private readonly ansage = inject(AnsageService);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  protected readonly modules = inject(ModulesService);
  protected readonly metrics = inject(MetricsService);

  protected readonly organization = signal<OrganizationInfo | null>(null);
  protected readonly entries = signal<JournalEntry[]>([]);
  protected readonly vouchers = signal<Voucher[]>([]);
  protected readonly loading = signal(true);
  protected readonly today = new Date();

  /**
   * Alle Blöcke in der Reihenfolge, die jemand sich eingerichtet hat -- auch
   * die ausgeblendeten, damit das Zahnrad sie wiederfindet.
   *
   * Solange niemand etwas verschoben hat, stehen sie wie die Reiter oben --
   * wer dort Finanzen nach vorn gezogen hat, findet sie auch hier oben. Die
   * eigene Reihenfolge der Übersicht geht darüber; die Buchungen hängen
   * zunächst hinten dran.
   */
  protected readonly alle = computed<Block[]>(() => {
    const module = this.modules
      .tabs()
      .filter((tab) => this.metrics.supports(tab.path))
      .map((tab) => ({ key: tab.key, label: tab.label, path: tab.path }));
    const wieReiter = this.preferences.sortiere('module', module, (block) => block.key);
    return this.preferences.sortiere(REIHENFOLGE, [...wieReiter, BUCHUNGEN], (block) => block.key);
  });

  protected readonly ausgeblendet = computed(() => new Set(this.preferences.liste(AUSGEBLENDET)));

  /** Was auf der Seite steht: alles, was nicht ausgeblendet ist. */
  protected readonly bloecke = computed(() =>
    this.alle().filter((block) => !this.ausgeblendet().has(block.key)),
  );

  /** Ob das Feld hinter dem Zahnrad offen ist. */
  protected readonly einrichten = signal(false);

  async ngOnInit(): Promise<void> {
    // Jede Aktion führt dorthin, wo der Bogen steht, und klappt ihn gleich
    // auf -- deshalb das neu=1. Ein Knopf, der nichts tut, ist schlimmer als
    // keiner: Hier stand bis zuletzt „Kommen stempeln", und dahinter lag
    // nichts, weil es die Zeiterfassung noch nicht gibt.
    this.actions.set('Aktion', [
      {
        label: 'Beleg einreichen',
        icon: 'beleg',
        primary: true,
        run: () => this.open('/finanzen/belege'),
      },
      { label: 'Aufgabe anlegen', icon: 'aufgaben', run: () => this.open('/aufgaben') },
      { label: 'Termin anlegen', icon: 'kalender', run: () => this.open('/kalender') },
      { label: 'Kontakt anlegen', icon: 'kontakte', run: () => this.open('/kontakte') },
      // Planen heißt: die Dienste der nächsten Wochen sehen und besetzen.
      // Dafür gibt es keinen Bogen, die Seite selbst ist die Aktion.
      {
        label: 'Personal planen',
        icon: 'personal',
        run: () => void this.router.navigate(['/personal']),
      },
      { label: 'Dokument hochladen', icon: 'dokumente', run: () => this.open('/dokumente') },
    ]);

    const [organization, entries, vouchers] = await Promise.all([
      this.orNull(this.house.organization()),
      this.orNull(this.journal.entries(5)),
      this.orNull(this.voucherList.unbooked(50)),
    ]);

    this.organization.set(organization);
    this.entries.set(entries ?? []);
    this.vouchers.set(vouchers ?? []);
    this.loading.set(false);
  }

  /** Vom Griff oder der Tastatur: Die Blöcke folgen sofort, das Konto danach. */
  protected umsortiert(ids: readonly string[]): void {
    void this.preferences.merke(REIHENFOLGE, ids);
  }

  /**
   * Aus dem Menü: einen Platz hoch oder runter -- unter den sichtbaren
   * Blöcken, denn über einen ausgeblendeten hinweg sähe man keinen Schritt.
   */
  protected verschiebe(block: Block, richtung: -1 | 1): void {
    const sichtbar = this.bloecke().map((b) => b.key);
    const von = sichtbar.indexOf(block.key);
    const nach = von + richtung;
    if (von < 0 || nach < 0 || nach >= sichtbar.length) {
      return;
    }

    // Die Reihenfolge aller Blöcke, der bewegte vor bzw. hinter seinem Nachbarn.
    const ids = this.alle()
      .map((b) => b.key)
      .filter((key) => key !== block.key);
    const nachbar = ids.indexOf(sichtbar[nach]);
    ids.splice(richtung < 0 ? nachbar : nachbar + 1, 0, block.key);

    void this.preferences.merke(REIHENFOLGE, ids);
    this.ansage.sage(`${block.label}, Platz ${nach + 1} von ${sichtbar.length}`);
  }

  protected ausblenden(block: Block): void {
    void this.preferences.merke(AUSGEBLENDET, [...this.ausgeblendet(), block.key]);
    this.ansage.sage(`${block.label} ausgeblendet. Wieder einblenden über „Übersicht einrichten".`);
  }

  protected zeige(block: Block, sichtbar: boolean): void {
    const ohne = [...this.ausgeblendet()].filter((key) => key !== block.key);
    const neu = sichtbar ? ohne : [...ohne, block.key];
    void (neu.length
      ? this.preferences.merke(AUSGEBLENDET, neu)
      : this.preferences.vergiss(AUSGEBLENDET));
  }

  /** Zurück auf die Reihenfolge der Reiter, alles sichtbar. */
  protected zuruecksetzen(): void {
    void this.preferences.vergiss(REIHENFOLGE);
    void this.preferences.vergiss(AUSGEBLENDET);
    this.einrichten.set(false);
  }

  /** Ein Klick daneben schließt das Feld -- wie bei jedem Aufklappmenü. */
  protected aussen(event: MouseEvent): void {
    const feld = this.host.nativeElement.querySelector('[data-einrichten]');
    if (this.einrichten() && feld && !feld.contains(event.target as Node)) {
      this.einrichten.set(false);
    }
  }

  private open(path: string): void {
    void this.router.navigate([path], { queryParams: { neu: 1 } });
  }

  private async orNull<T>(request: Promise<T>): Promise<T | null> {
    try {
      return await request;
    } catch {
      // Ein abgeschaltetes Modul antwortet mit 404 -- die Kachel bleibt dann leer.
      return null;
    }
  }
}
