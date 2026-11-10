import { CurrencyPipe } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { KskReport, KskService } from '../../core/ksk.service';
import { messagesFrom } from '../calendar/event-dialog.component';
import { MeldungComponent } from '../../shared/meldung.component';

/**
 * Die Künstlersozialabgabe eines Jahres.
 *
 * Zusammengezählt wird, was das Haus gekennzeichnet hat. Daneben steht, wer
 * künstlerisch mit dem Haus zu tun hat, Geld bekommen hat und *nicht*
 * gekennzeichnet ist — damit die Frage gestellt wird und nicht übersehen.
 */
@Component({
  selector: 'souffleur-ksk',
  imports: [CurrencyPipe, FormsModule, MeldungComponent],
  templateUrl: './ksk.component.html',
})
export class KskComponent implements OnInit {
  private readonly ksk = inject(KskService);

  protected readonly report = signal<KskReport | null>(null);
  protected readonly year = signal(new Date().getFullYear());
  protected readonly loading = signal(true);
  protected readonly busy = signal(false);
  protected readonly problems = signal<string[]>([]);

  /** Kein Zugang: Die Auswertung bleibt der Leitung vorbehalten. */
  protected readonly forbidden = signal(false);

  /** Der Satz, wie er gerade eingetippt wird. */
  protected readonly percent = signal('');

  async ngOnInit(): Promise<void> {
    await this.reload();
    this.loading.set(false);
  }

  protected async reload(): Promise<void> {
    this.problems.set([]);

    try {
      const report = await this.ksk.report(this.year());
      this.report.set(report);
      this.percent.set(report.rate === null ? '' : String(report.rate));
      this.forbidden.set(false);
    } catch (error) {
      if ((error as { status?: number }).status === 403) {
        this.forbidden.set(true);
      } else {
        this.problems.set(messagesFrom(error));
      }
    }
  }

  protected async shiftYear(years: number): Promise<void> {
    this.year.update((current) => current + years);
    await this.reload();
  }

  protected async saveRate(): Promise<void> {
    const value = Number(this.percent().replace(',', '.'));

    if (!this.percent().trim() || Number.isNaN(value)) {
      this.problems.set(['Der Abgabesatz fehlt.']);
      return;
    }

    this.busy.set(true);
    this.problems.set([]);

    try {
      await this.ksk.setRate(this.year(), value);
      await this.reload();
    } catch (error) {
      this.problems.set(messagesFrom(error));
    } finally {
      this.busy.set(false);
    }
  }
}
