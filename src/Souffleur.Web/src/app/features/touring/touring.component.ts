import { CurrencyPipe, DatePipe } from '@angular/common';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { AuthService } from '../../core/auth.service';
import { CatalogEntry, CatalogsService } from '../../core/catalogs.service';
import { Contact, ContactsService } from '../../core/contacts.service';
import { Person, PeopleService } from '../../core/people.service';
import { Production, ProductionsService } from '../../core/productions.service';
import {
  MemberInput,
  PerformanceInput,
  StatusInfo,
  Tour,
  TourStop,
  TourStopStatus,
  TouringService,
} from '../../core/touring.service';
import { Voucher, VouchersService } from '../../core/vouchers.service';
import { DialogComponent } from '../../shared/dialog.component';
import { LesezeichenComponent } from '../../shared/lesezeichen.component';
import { BookmarkDraft, BookmarksService } from '../../core/bookmarks.service';
import { IconComponent } from '../../shared/icon.component';
import { MeldungComponent } from '../../shared/meldung.component';
import { MetricStripComponent } from '../../shared/metric-strip.component';
import { ActionsRegistry } from '../../shell/actions.registry';
import { messagesFrom } from '../calendar/event-dialog.component';

/** Der Bogen für eine Tournee, so wie er im Fenster steht. */
interface TourDraft {
  title: string;
  productionId: string | null;
  notes: string;
  members: MemberInput[];
}

/** Eine Vorstellung im Bogen: Tag, Uhrzeit, Dauer -- so denkt die Disposition. */
interface PerformanceDraft {
  date: string;
  time: string;
  minutes: number;
  note: string;
}

interface StopDraft {
  venueId: string;
  contactPersonId: string | null;
  status: number;
  arrivalDate: string;
  departureDate: string;
  fee: string;
  hostCoversTravel: boolean;
  hostCoversAccommodation: boolean;
  voucherId: string | null;
  notes: string;
  performances: PerformanceDraft[];
}

/** Ausgangsbeleg -- dieselbe Zahl wie VoucherDirection.Outgoing am Server. */
const OUTGOING = 2;

/**
 * Tourneen und Gastspiele.
 *
 * Eine Tournee ist die Klammer, das Gastspiel der Ort: Spielstätte aus dem
 * Adressbuch, Vorstellungen im Kalender, Gage am Beleg, Reisende als
 * Beteiligte. Diese Seite legt beides an und zeigt, wie weit jedes Gastspiel
 * ist -- geplant wird hier, gesehen wird es überall.
 */
@Component({
  selector: 'souffleur-touring',
  imports: [
    CurrencyPipe,
    DatePipe,
    FormsModule,
    RouterLink,
    DialogComponent,
    LesezeichenComponent,
    IconComponent,
    MeldungComponent,
    MetricStripComponent,
  ],
  templateUrl: './touring.component.html',
  host: { class: 'block h-full min-h-0' },
})
export class TouringComponent implements OnInit {
  private readonly touring = inject(TouringService);
  private readonly catalogs = inject(CatalogsService);
  private readonly contacts = inject(ContactsService);
  private readonly peopleList = inject(PeopleService);
  private readonly productionList = inject(ProductionsService);
  private readonly vouchersList = inject(VouchersService);
  private readonly actions = inject(ActionsRegistry);
  private readonly bookmarks = inject(BookmarksService);
  private readonly auth = inject(AuthService);
  private readonly route = inject(ActivatedRoute);

  protected readonly tours = signal<Tour[]>([]);
  protected readonly statuses = signal<StatusInfo[]>([]);
  protected readonly productions = signal<Production[]>([]);
  protected readonly functions = signal<CatalogEntry[]>([]);
  protected readonly venues = signal<Contact[]>([]);
  protected readonly people = signal<Person[]>([]);
  protected readonly vouchers = signal<Voucher[]>([]);

  protected readonly loading = signal(true);
  protected readonly blocked = signal(false);
  protected readonly busy = signal(false);
  protected readonly problems = signal<string[]>([]);
  protected readonly includeSettled = signal(true);

  /** Welche Tournee aufgeklappt ist -- die anderen zeigen nur ihre Zeile. */
  protected readonly openTour = signal<string | null>(null);

  /** Lesezeichen, die beim Anlegen gesammelt werden -- Tournee oder Gastspiel gibt es noch nicht. */
  protected readonly pendingTourBookmarks = signal<BookmarkDraft[]>([]);
  protected readonly pendingStopBookmarks = signal<BookmarkDraft[]>([]);

  /**
   * Was gerade zum Löschen ansteht. Löschen fragt nach, wie bei den
   * Produktionen: Eine Tournee nimmt ihre Gastspiele mit, und die stehen
   * im Kalender.
   */
  protected readonly removing = signal<{ tour: Tour } | { stop: TourStop } | null>(null);

  protected readonly editingTour = signal<string | null>(null);
  protected readonly tourDraft = signal<TourDraft>(emptyTour());

  /** Welches Gastspiel geändert wird ('neu' = anlegen) und für welche Tournee. */
  protected readonly editingStop = signal<string | null>(null);
  protected readonly stopTourId = signal<string | null>(null);
  protected readonly stopDraft = signal<StopDraft>(emptyStop());

  protected readonly canManage = computed(() => this.auth.can('TouringManage'));

  protected readonly stopStatus = TourStopStatus;

  protected readonly summary = computed(() => {
    const stops = this.tours().flatMap((t) => t.stops);
    return {
      tours: this.tours().length,
      binding: stops.filter((s) => s.isBinding).length,
      open: stops.filter(
        (s) => s.status === TourStopStatus.Inquiry || s.status === TourStopStatus.Offered,
      ).length,
    };
  });

  async ngOnInit(): Promise<void> {
    this.actions.set(
      'Touring',
      this.canManage()
        ? [{ label: 'Tournee anlegen', icon: 'touring', primary: true, run: () => this.startTour(null) }]
        : [],
      [
        { label: 'Produktionen', icon: 'produktion', path: '/produktionen' },
        { label: 'Spielstätten', icon: 'kontakte', path: '/kontakte' },
        { label: 'Kalender', icon: 'kalender', path: '/kalender' },
      ],
    );

    try {
      this.statuses.set(await this.touring.statuses());
    } catch {
      // 404 heißt: Das Modul Touring ist in diesem Haus abgeschaltet.
      this.blocked.set(true);
      this.loading.set(false);
      return;
    }

    await Promise.all([this.reload(), this.loadLists()]);
    this.loading.set(false);

    if (this.route.snapshot.queryParamMap.get('neu')) {
      this.startTour(null);
    }
  }

  protected async reload(): Promise<void> {
    try {
      this.tours.set(await this.touring.tours(this.includeSettled()));
    } catch (error) {
      this.problems.set(messagesFrom(error));
    }
  }

  private async loadLists(): Promise<void> {
    // Was fehlt, fehlt still: Ohne Kontaktrecht gibt es keine Spielstätten
    // zur Auswahl, aber die Liste der Tourneen bleibt lesbar.
    const [productions, functions, venues, people, vouchers] = await Promise.all([
      this.productionList.choices(),
      this.catalogs.entries('ParticipationRole').catch(() => []),
      this.contacts.list().catch(() => []),
      this.peopleList.load().catch(() => []),
      this.vouchersList.all().catch(() => []),
    ]);

    this.productions.set(productions);
    this.functions.set(functions);
    // Spielstätten sind Kontakte mit dieser Rolle -- die Rolle ist das, was
    // das Adressbuch zur Liste der Spielstätten macht.
    this.venues.set(venues.filter((c) => c.isActive && c.roles.some((r) => r.key === 'Venue')));
    this.people.set(people);
    this.vouchers.set(vouchers.filter((v) => v.direction === OUTGOING));
  }

  protected toggle(tour: Tour): void {
    this.openTour.update((current) => (current === tour.id ? null : tour.id));
  }

  // --- Tournee ---

  protected startTour(tour: Tour | null): void {
    this.problems.set([]);
    this.editingTour.set(tour?.id ?? 'neu');
    this.tourDraft.set(
      tour
        ? {
            title: tour.title,
            productionId: tour.productionId,
            notes: tour.notes ?? '',
            members: tour.members.map((m) => ({
              contactId: m.contactId,
              functionId: m.functionId,
              status: m.status,
              note: m.note,
            })),
          }
        : emptyTour(),
    );
  }

  protected patchTour(change: Partial<TourDraft>): void {
    this.tourDraft.update((current) => ({ ...current, ...change }));
  }

  protected addMember(): void {
    const taken = new Set(this.tourDraft().members.map((m) => m.contactId));
    const next = this.people().find((p) => !taken.has(p.id));
    if (!next) {
      return;
    }

    this.patchTour({
      members: [...this.tourDraft().members, { contactId: next.id, functionId: null, status: 1 }],
    });
  }

  protected patchMember(index: number, change: Partial<MemberInput>): void {
    const members = this.tourDraft().members.map((m, i) => (i === index ? { ...m, ...change } : m));
    this.patchTour({ members });
  }

  protected removeMember(index: number): void {
    this.patchTour({ members: this.tourDraft().members.filter((_, i) => i !== index) });
  }

  protected async saveTour(): Promise<void> {
    const draft = this.tourDraft();
    if (!draft.title.trim()) {
      this.problems.set(['Eine Tournee braucht einen Titel.']);
      return;
    }

    this.busy.set(true);
    this.problems.set([]);

    const input = {
      title: draft.title.trim(),
      productionId: draft.productionId || null,
      notes: draft.notes.trim() || null,
      members: draft.members.map((m) => ({
        contactId: m.contactId,
        functionId: m.functionId || null,
        status: m.status ?? 1,
        note: m.note?.trim() || null,
      })),
    };

    try {
      const id = this.editingTour();
      const saved =
        id === 'neu' ? await this.touring.createTour(input) : await this.touring.updateTour(id!, input);
      if (id === 'neu') {
        for (const draft of this.pendingTourBookmarks()) {
          await this.bookmarks.add('Tour', saved.id, draft);
        }
      }
      this.pendingTourBookmarks.set([]);
      this.editingTour.set(null);
      await this.reload();
      this.openTour.set(saved.id);
    } catch (error) {
      this.problems.set(messagesFrom(error));
    } finally {
      this.busy.set(false);
    }
  }

  protected askRemoveTour(tour: Tour): void {
    this.problems.set([]);
    this.removing.set({ tour });
  }

  protected async removeTour(tour: Tour): Promise<void> {
    this.problems.set([]);
    try {
      await this.touring.removeTour(tour.id);
      this.removing.set(null);
      await this.reload();
    } catch (error) {
      this.problems.set(messagesFrom(error));
    }
  }

  // --- Gastspiel ---

  protected startStop(tour: Tour, stop: TourStop | null): void {
    this.problems.set([]);
    this.stopTourId.set(tour.id);
    this.editingStop.set(stop?.id ?? 'neu');
    this.stopDraft.set(
      stop
        ? {
            venueId: stop.venueId,
            contactPersonId: stop.contactPersonId,
            status: stop.status,
            arrivalDate: stop.arrivalDate ?? '',
            departureDate: stop.departureDate ?? '',
            fee: stop.fee === null ? '' : String(stop.fee),
            hostCoversTravel: stop.hostCoversTravel,
            hostCoversAccommodation: stop.hostCoversAccommodation,
            voucherId: stop.voucherId,
            notes: stop.notes ?? '',
            performances: stop.performances.map(toPerformanceDraft),
          }
        : { ...emptyStop(), venueId: this.venues()[0]?.id ?? '' },
    );
  }

  protected patchStop(change: Partial<StopDraft>): void {
    this.stopDraft.update((current) => ({ ...current, ...change }));
  }

  protected addPerformance(): void {
    const draft = this.stopDraft();
    const last = draft.performances.at(-1);

    // Der nächste Abend: einen Tag nach dem letzten, sonst der Anreisetag.
    const date = last ? shiftDay(last.date, 1) : draft.arrivalDate || today();
    this.patchStop({
      performances: [
        ...draft.performances,
        { date, time: last?.time ?? '19:30', minutes: last?.minutes ?? 120, note: '' },
      ],
    });
  }

  protected patchPerformance(index: number, change: Partial<PerformanceDraft>): void {
    const performances = this.stopDraft().performances.map((p, i) =>
      i === index ? { ...p, ...change } : p,
    );
    this.patchStop({ performances });
  }

  protected removePerformance(index: number): void {
    this.patchStop({ performances: this.stopDraft().performances.filter((_, i) => i !== index) });
  }

  protected async saveStop(): Promise<void> {
    const draft = this.stopDraft();
    if (!draft.venueId) {
      this.problems.set(['Ein Gastspiel braucht eine Spielstätte.']);
      return;
    }

    this.busy.set(true);
    this.problems.set([]);

    const performances: PerformanceInput[] = [];
    for (const p of draft.performances) {
      if (!p.date || !p.time) {
        this.problems.set(['Jede Vorstellung braucht Tag und Uhrzeit.']);
        this.busy.set(false);
        return;
      }

      const starts = new Date(`${p.date}T${p.time}:00`);
      const ends = new Date(starts.getTime() + Math.max(1, p.minutes) * 60_000);
      performances.push({ startsAt: starts.toISOString(), endsAt: ends.toISOString(), note: p.note.trim() || null });
    }

    const fee = draft.fee.trim() === '' ? null : Number(draft.fee.replace(',', '.'));
    if (fee !== null && Number.isNaN(fee)) {
      this.problems.set(['Die Gage ist keine Zahl.']);
      this.busy.set(false);
      return;
    }

    const input = {
      venueId: draft.venueId,
      contactPersonId: draft.contactPersonId || null,
      status: draft.status,
      arrivalDate: draft.arrivalDate || null,
      departureDate: draft.departureDate || null,
      fee,
      hostCoversTravel: draft.hostCoversTravel,
      hostCoversAccommodation: draft.hostCoversAccommodation,
      voucherId: draft.voucherId || null,
      notes: draft.notes.trim() || null,
      performances,
    };

    try {
      const id = this.editingStop();
      if (id === 'neu') {
        const created = await this.touring.createStop(this.stopTourId()!, input);
        for (const draft of this.pendingStopBookmarks()) {
          await this.bookmarks.add('TourStop', created.id, draft);
        }
      } else {
        await this.touring.updateStop(id!, input);
      }

      this.pendingStopBookmarks.set([]);
      this.editingStop.set(null);
      await this.reload();
    } catch (error) {
      this.problems.set(messagesFrom(error));
    } finally {
      this.busy.set(false);
    }
  }

  protected askRemoveStop(stop: TourStop): void {
    this.problems.set([]);
    this.removing.set({ stop });
  }

  protected async removeStop(stop: TourStop): Promise<void> {
    this.problems.set([]);
    try {
      await this.touring.removeStop(stop.id);
      this.removing.set(null);
      await this.reload();
    } catch (error) {
      this.problems.set(messagesFrom(error));
    }
  }

  /** Für die Nachfrage: eine Tournee, wenn es eine ist. */
  protected removingTour(): Tour | null {
    const r = this.removing();
    return r && 'tour' in r ? r.tour : null;
  }

  protected removingStop(): TourStop | null {
    const r = this.removing();
    return r && 'stop' in r ? r.stop : null;
  }

  protected cancel(): void {
    this.editingTour.set(null);
    this.editingStop.set(null);
    this.stopTourId.set(null);
    this.problems.set([]);
  }

  /** Von wann bis wann, als ein Satz -- für die Zeile der Tournee. */
  protected span(tour: Tour): string {
    if (!tour.startsOn) {
      return 'noch ohne Termine';
    }

    const from = day(tour.startsOn);
    return tour.endsOn && tour.endsOn !== tour.startsOn ? `${from} – ${day(tour.endsOn)}` : from;
  }

  protected travel(stop: TourStop): string {
    if (!stop.arrivalDate && !stop.departureDate) {
      return '';
    }

    const from = stop.arrivalDate ? day(stop.arrivalDate) : '?';
    const to = stop.departureDate ? day(stop.departureDate) : '?';
    return from === to ? from : `${from} – ${to}`;
  }

  /** Welchen Platz in der Farbreihe ein Stand bekommt. */
  protected slot(status: number): number {
    switch (status) {
      case TourStopStatus.Confirmed:
        return 3;
      case TourStopStatus.Settled:
        return 4;
      case TourStopStatus.Offered:
        return 2;
      case TourStopStatus.Inquiry:
        return 1;
      default:
        return 0;
    }
  }

  protected venueOf(stop: TourStop): string {
    return stop.venueCity ? `${stop.venueName}, ${stop.venueCity}` : stop.venueName;
  }
}

function emptyTour(): TourDraft {
  return { title: '', productionId: null, notes: '', members: [] };
}

function emptyStop(): StopDraft {
  return {
    venueId: '',
    contactPersonId: null,
    status: TourStopStatus.Inquiry,
    arrivalDate: '',
    departureDate: '',
    fee: '',
    hostCoversTravel: false,
    hostCoversAccommodation: false,
    voucherId: null,
    notes: '',
    performances: [],
  };
}

function toPerformanceDraft(p: { startsAt: string; endsAt: string; note: string | null }): PerformanceDraft {
  const starts = new Date(p.startsAt);
  const ends = new Date(p.endsAt);
  const pad = (n: number) => String(n).padStart(2, '0');

  return {
    date: `${starts.getFullYear()}-${pad(starts.getMonth() + 1)}-${pad(starts.getDate())}`,
    time: `${pad(starts.getHours())}:${pad(starts.getMinutes())}`,
    minutes: Math.max(1, Math.round((ends.getTime() - starts.getTime()) / 60_000)),
    note: p.note ?? '',
  };
}

function day(iso: string): string {
  return new Date(`${iso}T12:00:00Z`).toLocaleDateString('de-DE', {
    day: 'numeric',
    month: 'short',
    year: 'numeric',
  });
}

function today(): string {
  const now = new Date();
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}`;
}

function shiftDay(iso: string, days: number): string {
  const date = new Date(`${iso}T12:00:00Z`);
  date.setUTCDate(date.getUTCDate() + days);
  return date.toISOString().slice(0, 10);
}
