import { DatePipe } from '@angular/common';
import { Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { CalendarService, Conflict, isoDate } from '../../core/calendar.service';
import { DutiesService, DutySlot, PlannedDuty } from '../../core/duties.service';
import { Person, PeopleService } from '../../core/people.service';
import { DialogComponent } from '../../shared/dialog.component';
import { MetricStripComponent } from '../../shared/metric-strip.component';
import { ActionsRegistry } from '../../shell/actions.registry';
import { conflictsFrom, messagesFrom } from '../calendar/event-dialog.component';
import { KskComponent } from './ksk.component';
import { ZeitenComponent } from './zeiten.component';
import { AbwesenheitenComponent } from './abwesenheiten.component';
import { verfolgeAdresse } from '../../shared/sprungziel';
import { MeldungComponent } from '../../shared/meldung.component';
import { GroupsService } from '../../core/groups.service';

type View = 'dienste' | 'zeiten' | 'abwesenheiten' | 'ksk';

const VIEWS: readonly View[] = ['dienste', 'zeiten', 'abwesenheiten', 'ksk'];

interface DutyDay {
  readonly date: string;
  readonly entries: readonly PlannedDuty[];
}

/**
 * Das Modul Personal.
 *
 * Drei Fragen an dieselben Leute: Wer ist eingeteilt und wo fehlt jemand
 * (Dienste), was wurde gearbeitet (Zeiten), und was folgt daraus für die
 * Künstlersozialkasse.
 *
 * Der Dienstplan rechnet gegen den Kalender: Der Bedarf hängt an der
 * Terminkategorie, und eine Vorstellung ohne jede Einteilung taucht deshalb
 * auf — in einer Liste der Eingeteilten wäre sie unsichtbar.
 */
@Component({
  selector: 'souffleur-dienste',
  imports: [DatePipe,
    FormsModule,
    MetricStripComponent,
    ZeitenComponent,
    KskComponent,
    AbwesenheitenComponent,
    DialogComponent, MeldungComponent],
  templateUrl: './dienste.component.html',
})
export class DiensteComponent implements OnInit {
  private readonly duties = inject(DutiesService);
  private readonly calendar = inject(CalendarService);
  private readonly peopleList = inject(PeopleService);
  private readonly groupList = inject(GroupsService);
  private readonly actions = inject(ActionsRegistry);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly plan = signal<PlannedDuty[]>([]);
  protected readonly loading = signal(true);
  protected readonly blocked = signal(false);

  /** Geplant, gearbeitet, abwesend, abgerechnet. */
  protected readonly view = signal<View>('dienste');

  protected readonly people = signal<Person[]>([]);
  protected readonly problems = signal<string[]>([]);
  protected readonly busy = signal(false);

  /** Welche offene Stelle gerade besetzt wird. */
  protected readonly filling = signal<{ duty: PlannedDuty; slot: DutySlot } | null>(null);
  protected readonly chosen = signal('');

  /**
   * Die Kontakte der Gruppe, aus der für diese Stelle vorgeschlagen wird.
   * Null heißt: Die Stelle hat keine Gruppe, es stehen alle zur Wahl.
   */
  protected readonly gruppenKontakte = signal<ReadonlySet<string> | null>(null);

  /** Ob trotz Gruppe alle zur Wahl stehen sollen. */
  protected readonly alleZeigen = signal(false);

  /**
   * Was dagegensteht. Eine leere Liste heißt nicht „frei" -- erst nach einem
   * abgelehnten Versuch steht hier etwas.
   */
  protected readonly conflicts = signal<Conflict[]>([]);

  /** Dieselben vier Wochen, die auch die Kacheln zählen. */
  protected readonly until = (() => {
    const date = new Date();
    date.setDate(date.getDate() + 28);
    return date;
  })();

  protected readonly days = computed<DutyDay[]>(() => {
    const byDate = new Map<string, PlannedDuty[]>();

    for (const duty of this.plan()) {
      const list = byDate.get(duty.localDate);
      if (list) {
        list.push(duty);
      } else {
        byDate.set(duty.localDate, [duty]);
      }
    }

    return [...byDate.entries()]
      .sort((a, b) => a[0].localeCompare(b[0]))
      .map(([date, entries]) => ({
        date,
        entries: entries.sort((a, b) => a.startsAt.localeCompare(b.startsAt)),
      }));
  });

  protected readonly total = computed(() =>
    this.plan().reduce((sum, duty) => sum + duty.assigned.length, 0),
  );

  /** Wie viele Stellen in den vier Wochen unbesetzt sind. */
  protected readonly open = computed(() => this.plan().reduce((sum, duty) => sum + duty.open, 0));

  /** Wer noch nicht an diesem Termin steht. */
  protected readonly available = computed(() => {
    const duty = this.filling()?.duty;

    if (!duty) {
      return this.people();
    }

    const taken = new Set(duty.assigned.map((person) => person.contactId));
    const frei = this.people().filter((person) => !taken.has(person.id));

    const gruppe = this.gruppenKontakte();
    if (gruppe === null || this.alleZeigen()) {
      return frei;
    }

    // Leer heißt hier nicht „niemand darf", sondern „die Gruppe kennt
    // niemanden mit Kontakt" -- dann alle, sonst steht man vor einem leeren
    // Feld und weiß nicht, warum.
    const ausGruppe = frei.filter((person) => gruppe.has(person.id));
    return ausGruppe.length > 0 ? ausGruppe : frei;
  });

  /** Ob die Gruppe gerade niemanden beisteuert und deshalb alle zu sehen sind. */
  protected readonly gruppeLeer = computed(() => {
    const gruppe = this.gruppenKontakte();
    return gruppe !== null && !this.alleZeigen() && ![...gruppe].some((id) =>
      this.people().some((person) => person.id === id),
    );
  });

  async ngOnInit(): Promise<void> {
    this.actions.set(
      'Personal',
      [
        {
          label: 'Zeit stempeln',
          icon: 'uhr',
          primary: true,
          run: () => this.view.set('zeiten'),
        },
        {
          label: 'Abwesenheit beantragen',
          icon: 'kalender',
          run: () => this.view.set('abwesenheiten'),
        },
        {
          label: 'Im Kalender einteilen',
          icon: 'kalender',
          run: () => void this.router.navigate(['/kalender']),
        },
      ],
      [
        { label: 'Bedarf je Terminart', icon: 'bedarf', path: '/einstellungen/dienstbedarf' },
        { label: 'Funktionen am Termin', icon: 'funktion', path: '/einstellungen/listen/ParticipationRole' },
        { label: 'Leute des Hauses', path: '/kontakte' },
      ],
    );

    const today = new Date();

    try {
      this.plan.set(await this.duties.plan(asIso(today), asIso(this.until)));
      this.people.set(await this.peopleList.load());
    } catch {
      // Ohne Kalender gibt es keine Dienste -- das Modul ist dann abgeschaltet.
      this.blocked.set(true);
    } finally {
      this.loading.set(false);
    }

    // Die Glocke schickt Anträge hierher: `/personal?ansicht=abwesenheiten`.
    verfolgeAdresse(this.route, this.destroyRef, (params) => {
      const wanted = params.get('ansicht') as View | null;
      if (wanted && VIEWS.includes(wanted)) {
        this.view.set(wanted);
      }
    });
  }

  protected async startFilling(duty: PlannedDuty, slot: DutySlot): Promise<void> {
    this.problems.set([]);
    this.conflicts.set([]);
    this.chosen.set('');
    this.alleZeigen.set(false);
    this.gruppenKontakte.set(null);
    this.filling.set({ duty, slot });

    if (slot.groupId) {
      try {
        this.gruppenKontakte.set(new Set(await this.groupList.contactsOf(slot.groupId)));
      } catch {
        // Ohne Gruppenliste stehen eben alle zur Wahl -- besetzen geht trotzdem.
        this.gruppenKontakte.set(null);
      }
    }
  }

  protected cancel(): void {
    this.filling.set(null);
    this.conflicts.set([]);
    this.chosen.set('');
  }

  /**
   * Einteilen. `force` setzt eine gemeldete Doppelbelegung durch -- der Knopf
   * dafür erscheint erst, wenn der Server eine gemeldet hat.
   */
  protected async assign(force = false): Promise<void> {
    const open = this.filling();
    const contactId = this.chosen();

    if (!open?.duty.eventId || !contactId) {
      this.problems.set(['Es ist niemand gewählt.']);
      return;
    }

    this.busy.set(true);
    this.problems.set([]);

    try {
      await this.calendar.assign(open.duty.eventId, contactId, open.slot.functionId, force);
      this.cancel();
      await this.reload();
    } catch (error) {
      const occupied = conflictsFrom(error);

      if (occupied.length > 0) {
        // Nicht abgelehnt, sondern angehalten: Der Server hat 409 geantwortet.
        this.conflicts.set(occupied);
      } else {
        this.problems.set(messagesFrom(error));
      }
    } finally {
      this.busy.set(false);
    }
  }

  protected async unassign(duty: PlannedDuty, contactId: string): Promise<void> {
    if (!duty.eventId) {
      return;
    }

    this.busy.set(true);
    this.problems.set([]);

    try {
      await this.calendar.unassign(duty.eventId, contactId);
      await this.reload();
    } catch (error) {
      this.problems.set(messagesFrom(error));
    } finally {
      this.busy.set(false);
    }
  }

  protected conflictTime(conflict: Conflict): string {
    const day = new Date(conflict.startsAt);
    return `${day.toLocaleDateString('de-DE', { day: 'numeric', month: 'numeric' })}, ${day.toLocaleTimeString('de-DE', { hour: '2-digit', minute: '2-digit' })}`;
  }

  private async reload(): Promise<void> {
    const today = new Date();
    this.plan.set(await this.duties.plan(asIso(today), asIso(this.until)));
  }
}

/** Ortszeit, nicht UTC: toISOString() verschiebt den Tag. */
function asIso(date: Date): string {
  return isoDate(date.getFullYear(), date.getMonth() + 1, date.getDate());
}
