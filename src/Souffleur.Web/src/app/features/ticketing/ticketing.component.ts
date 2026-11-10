import { CurrencyPipe, DatePipe, DecimalPipe } from '@angular/common';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import {
  CapacitySource,
  Performance,
  TicketingProvider,
  TicketingService,
  TicketingSettings,
} from '../../core/ticketing.service';
import { MetricStripComponent } from '../../shared/metric-strip.component';
import { ActionsRegistry } from '../../shell/actions.registry';
import { messagesFrom } from '../calendar/event-dialog.component';
import { MeldungComponent } from '../../shared/meldung.component';

/**
 * Verkäufe und Auslastung aus dem Vorverkauf des Hauses.
 *
 * Das Modul lässt sich abschalten -- Häuser ohne eigenen Kartenverkauf haben
 * hier nichts zu sehen, und auch ein Haus mit Vorverkauf muss ihn nicht
 * anbinden. Deshalb beginnt die Seite bei „kein Vorverkauf" und nicht bei
 * einem Fehler.
 */
@Component({
  selector: 'souffleur-ticketing',
  imports: [CurrencyPipe, DatePipe, DecimalPipe, FormsModule, MetricStripComponent, MeldungComponent],
  templateUrl: './ticketing.component.html',
})
export class TicketingComponent implements OnInit {
  private readonly ticketing = inject(TicketingService);
  private readonly actions = inject(ActionsRegistry);
  private readonly route = inject(ActivatedRoute);

  protected readonly settings = signal<TicketingSettings | null>(null);
  protected readonly performances = signal<Performance[]>([]);
  protected readonly loading = signal(true);
  protected readonly blocked = signal(false);
  protected readonly busy = signal(false);
  protected readonly problems = signal<string[]>([]);

  /** Offen, solange jemand die Anbindung einrichtet. */
  protected readonly setup = signal(false);
  protected readonly provider = signal<number>(TicketingProvider.None);
  protected readonly baseUrl = signal('');
  protected readonly organizer = signal('');
  protected readonly token = signal('');
  protected readonly cacheSeconds = signal('60');

  protected readonly providers = TicketingProvider;
  protected readonly sources = CapacitySource;

  /** Nur Vorstellungen, die noch kommen -- der Rest steht weiter unten. */
  protected readonly upcoming = computed(() => {
    const today = new Date().toISOString().slice(0, 10);
    return this.performances().filter((p) => p.date.slice(0, 10) >= today);
  });

  protected readonly past = computed(() => {
    const today = new Date().toISOString().slice(0, 10);
    return this.performances()
      .filter((p) => p.date.slice(0, 10) < today)
      .reverse();
  });

  async ngOnInit(): Promise<void> {
    this.actions.set('Ticketing', [
      { label: 'Vorverkauf einrichten', icon: 'plus', primary: true, run: () => this.startSetup() },
      { label: 'Zahlen neu holen', icon: 'download', run: () => this.reload() },
      { label: 'Verbindung prüfen', icon: 'abo', run: () => this.check() },
    ]);

    try {
      this.settings.set(await this.ticketing.settings());
    } catch {
      // 404 heißt hier: Das Modul Ticketing ist in diesem Haus abgeschaltet.
      this.blocked.set(true);
      this.loading.set(false);
      return;
    }

    await this.reload();
    this.loading.set(false);

    if (this.route.snapshot.queryParamMap.get('neu') || !this.settings()?.isConfigured) {
      this.startSetup();
    }
  }

  protected async reload(): Promise<void> {
    if (!this.settings()?.isConfigured) {
      this.performances.set([]);
      return;
    }

    this.busy.set(true);
    try {
      this.performances.set(await this.ticketing.performances());
      this.problems.set([]);
    } catch (error) {
      this.problems.set(messagesFrom(error));
      this.performances.set([]);
    } finally {
      this.busy.set(false);
    }
  }

  protected startSetup(): void {
    const current = this.settings();
    this.problems.set([]);
    this.provider.set(current?.provider ?? TicketingProvider.None);
    this.baseUrl.set(current?.baseUrl ?? '');
    this.organizer.set(current?.organizer ?? '');
    this.token.set('');
    this.cacheSeconds.set(String(current?.cacheSeconds ?? 60));
    this.setup.set(true);
  }

  protected cancelSetup(): void {
    this.setup.set(false);
    this.problems.set([]);
  }

  protected async save(): Promise<void> {
    this.busy.set(true);
    this.problems.set([]);

    try {
      const saved = await this.ticketing.save({
        provider: this.provider(),
        baseUrl: this.baseUrl().trim() || null,
        organizer: this.organizer().trim() || null,

        // Leer heißt „unverändert". Ein einmal hinterlegtes Token kommt nie
        // wieder in die Oberfläche, also kann es auch nicht zurückgeschickt
        // werden -- versehentlich leeren soll es sich trotzdem nicht.
        token: this.token().trim() || null,
        cacheSeconds: Number(this.cacheSeconds()) || 0,
      });

      this.settings.set(saved);
      this.setup.set(false);
      await this.reload();
    } catch (error) {
      this.problems.set(messagesFrom(error));
    } finally {
      this.busy.set(false);
    }
  }

  protected async check(): Promise<void> {
    this.busy.set(true);
    this.problems.set([]);

    try {
      this.settings.set(await this.ticketing.check());
    } catch (error) {
      this.problems.set(messagesFrom(error));
    } finally {
      this.busy.set(false);
    }
  }

  protected sourceLabel(performance: Performance): string {
    if (performance.source === CapacitySource.SeatingPlan) {
      return 'Platzzahl aus dem Saalplan';
    }
    if (performance.source === CapacitySource.Quotas) {
      return 'Platzzahl aus den Kontingenten';
    }
    return performance.capacityNote ?? 'Platzzahl unbestimmt';
  }
}
