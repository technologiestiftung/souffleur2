import { HttpClient } from '@angular/common/http';
import { DatePipe } from '@angular/common';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { AbsencesService, Holiday, HouseHoliday } from '../../core/absences.service';
import { AuthService } from '../../core/auth.service';
import { MeldungComponent } from '../../shared/meldung.component';
import { ActionsRegistry } from '../../shell/actions.registry';
import { messagesFrom } from '../calendar/event-dialog.component';
import { einstellungsBereiche } from './bereiche';

interface FederalState {
  value: number;
  key: string;
  label: string;
}

const MONTHS = [
  'Januar',
  'Februar',
  'März',
  'April',
  'Mai',
  'Juni',
  'Juli',
  'August',
  'September',
  'Oktober',
  'November',
  'Dezember',
];

/**
 * Die Tage, an denen niemand Soll hat: die gesetzlichen Feiertage des
 * Bundeslands und die, die das Haus allen schenkt.
 *
 * Beides entscheidet darüber, was ein Urlaub kostet. Wer Heiligabend frei
 * hat, soll ihn nicht als Urlaub nehmen müssen.
 */
@Component({
  selector: 'souffleur-freie-tage',
  imports: [DatePipe, FormsModule, MeldungComponent],
  templateUrl: './freie-tage.component.html',
})
export class FreieTageComponent implements OnInit {
  private readonly http = inject(HttpClient);
  private readonly absences = inject(AbsencesService);
  private readonly actions = inject(ActionsRegistry);
  private readonly auth = inject(AuthService);

  protected readonly months = MONTHS;

  protected readonly loading = signal(true);
  protected readonly busy = signal(false);
  protected readonly problems = signal<string[]>([]);

  protected readonly states = signal<FederalState[]>([]);
  protected readonly state = signal(0);
  protected readonly year = signal(new Date().getFullYear());
  protected readonly holidays = signal<Holiday[]>([]);
  protected readonly houseDays = signal<HouseHoliday[]>([]);

  protected readonly draft = signal({ month: 12, day: 24, name: '' });

  protected readonly mayChangeState = computed(() => this.auth.can('HouseManage'));
  protected readonly mayChangeDays = computed(() => this.auth.can('PersonnelRecords'));

  async ngOnInit(): Promise<void> {
    this.actions.set('Einstellungen', [], einstellungsBereiche(this.auth.can('AccountsManage')));

    try {
      const [states, house] = await Promise.all([
        firstValueFrom(this.http.get<FederalState[]>('/api/organization/federal-states')),
        firstValueFrom(this.http.get<{ federalState: number }>('/api/organization')),
      ]);
      this.states.set(states);
      this.state.set(house.federalState);
      await this.reload();
    } catch (error) {
      this.problems.set(messagesFrom(error));
    } finally {
      this.loading.set(false);
    }
  }

  protected async reload(): Promise<void> {
    this.holidays.set(await this.absences.holidays(this.year()));
    if (this.mayChangeDays()) {
      this.houseDays.set(await this.absences.houseHolidays());
    }
  }

  protected async shiftYear(by: number): Promise<void> {
    this.year.update((y) => y + by);
    await this.reload();
  }

  protected async saveState(value: number): Promise<void> {
    await this.run(async () => {
      await firstValueFrom(
        this.http.put('/api/organization/federal-state', { federalState: Number(value) }),
      );
      this.state.set(Number(value));
    });
  }

  protected async addDay(): Promise<void> {
    const d = this.draft();
    await this.run(async () => {
      await this.absences.addHouseHoliday(Number(d.month), Number(d.day), d.name);
      this.draft.set({ month: 12, day: 24, name: '' });
    });
  }

  protected async removeDay(day: HouseHoliday): Promise<void> {
    await this.run(() => this.absences.removeHouseHoliday(day.id));
  }

  protected patch(change: Partial<{ month: number; day: number; name: string }>): void {
    this.draft.update((current) => ({ ...current, ...change }));
  }

  private async run(action: () => Promise<unknown>): Promise<void> {
    this.busy.set(true);
    this.problems.set([]);

    try {
      await action();
      await this.reload();
    } catch (error) {
      this.problems.set(messagesFrom(error));
    } finally {
      this.busy.set(false);
    }
  }
}
