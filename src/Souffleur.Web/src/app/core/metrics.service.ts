import { Injectable, inject } from '@angular/core';
import { BankService } from './bank.service';
import { CalendarService } from './calendar.service';
import { ContactsService } from './contacts.service';
import { JournalService } from './journal.service';
import {
  MetricTile,
  calendarTiles,
  contactTiles,
  dutyTiles,
  financeTiles,
  productionTiles,
  taskTiles,
  ticketingTiles,
  touringTiles,
} from './metrics';
import { TasksService } from './tasks.service';
import { TicketingService } from './ticketing.service';
import { TouringService } from './touring.service';
import { ProductionsService } from './productions.service';
import { VouchersService } from './vouchers.service';

/** Welche Module Kennzahlen liefern. */
const WITH_METRICS = [
  'finanzen',
  'ticketing',
  'personal',
  'aufgaben',
  'kontakte',
  'kalender',
  'touring',
  'produktionen',
];

/**
 * Holt die Zahlen, aus denen die Kacheln werden.
 *
 * Jedes Modul beantwortet seine eigene Frage; was es nicht beantwortet --
 * weil es abgeschaltet ist oder niemand die Rechte hat -- fällt still weg.
 * Die Übersicht fragt alle Module nacheinander und zeigt, was antwortet.
 */
@Injectable({ providedIn: 'root' })
export class MetricsService {
  private readonly journal = inject(JournalService);
  private readonly vouchers = inject(VouchersService);
  private readonly bank = inject(BankService);
  private readonly tasks = inject(TasksService);
  private readonly contacts = inject(ContactsService);
  private readonly calendar = inject(CalendarService);
  private readonly ticketing = inject(TicketingService);
  private readonly touring = inject(TouringService);
  private readonly productions = inject(ProductionsService);

  /** Welche Module Kennzahlen liefern -- der Rest zeigt gar keine Reihe. */
  supports(module: string): boolean {
    return WITH_METRICS.includes(module);
  }

  /** Die Kacheln eines Moduls, benannt nach seinem Pfad in der Oberfläche. */
  async tilesFor(module: string): Promise<MetricTile[]> {
    try {
      switch (module) {
        case 'finanzen':
          return await this.finance();
        case 'personal':
          return await this.duties();
        case 'aufgaben':
          return taskTiles(await this.tasks.snapshot());
        case 'ticketing':
          return ticketingTiles(await this.ticketing.performances());
        case 'touring':
          return touringTiles(await this.touring.tours());
        case 'produktionen':
          return productionTiles(await this.productions.list());
        case 'kontakte':
          return contactTiles(await this.contacts.list(), await this.contacts.roles());
        case 'kalender':
          return await this.calendarMonth();
        default:
          return [];
      }
    } catch {
      // Modul aus, kein Recht, Server weg: dann bleibt die Reihe leer.
      return [];
    }
  }

  private async finance(): Promise<MetricTile[]> {
    const [entries, vouchers, transactions, costCenters] = await Promise.all([
      this.journal.entries(500),
      this.vouchers.all(),
      this.bank.allTransactions(),
      this.journal.costCenters(),
    ]);

    return financeTiles(entries, vouchers, transactions, costCenters);
  }

  /**
   * Die Dienste der nächsten vier Wochen.
   *
   * Vier Wochen, weil ein Dienstplan so weit reicht -- der Monatserste ist
   * hier die falsche Grenze: Am 30. stünde sonst fast nichts mehr da.
   */
  private async duties(): Promise<MetricTile[]> {
    const today = new Date();
    const until = new Date(today);
    until.setDate(until.getDate() + 28);

    const occurrences = await this.calendar.occurrencesIn(iso(today), iso(until));

    return dutyTiles(occurrences, 'in den nächsten vier Wochen');
  }

  private async calendarMonth(): Promise<MetricTile[]> {
    const today = new Date();
    const from = new Date(today.getFullYear(), today.getMonth(), 1);
    const to = new Date(today.getFullYear(), today.getMonth() + 1, 0);

    const occurrences = await this.calendar.occurrencesIn(iso(from), iso(to));
    const month = from.toLocaleDateString('de', { month: 'long' });

    return calendarTiles(occurrences, `im ${month}`);
  }
}

/** Ortszeit, nicht UTC: toISOString() verschiebt den Monatsersten. */
function iso(date: Date): string {
  const month = String(date.getMonth() + 1).padStart(2, '0');
  const day = String(date.getDate()).padStart(2, '0');
  return `${date.getFullYear()}-${month}-${day}`;
}
