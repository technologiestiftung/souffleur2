import { DatePipe } from '@angular/common';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import {
  Absence,
  AbsenceKindInfo,
  AbsenceStatus,
  AbsencesService,
  Adjustment,
  Balance,
  Employment,
  WEEKDAYS,
  asDays,
  isoDay,
  signedHours,
} from '../../core/absences.service';
import { AuthService } from '../../core/auth.service';
import { Person, PeopleService } from '../../core/people.service';
import { DialogComponent } from '../../shared/dialog.component';
import { MeldungComponent } from '../../shared/meldung.component';
import { messagesFrom } from '../calendar/event-dialog.component';

/** Was der Antragsbogen gerade hält. */
interface Draft {
  contactId: string;
  kind: number;
  startDate: string;
  endDate: string;
  isHalfDay: boolean;
  deputyId: string;
  note: string;
  certificateReceived: boolean;
}

/** Was gerade offen ist: ein Bogen, eine Rückfrage, oder nichts. */
type Open =
  | { what: 'antrag' }
  | { what: 'krank' }
  | { what: 'ablehnen'; absence: Absence }
  | { what: 'aufheben'; absence: Absence }
  | { what: 'nachfuehren'; absence: Absence }
  | { what: 'vertrag'; person: Balance | { contactId: string; name: string } };

/**
 * Urlaub, Krankheit, Freizeitausgleich -- beantragen, entscheiden, und was
 * daraus für die Konten folgt.
 *
 * Drei Kreise auf einer Seite: Das Eigene sieht jede Person. Offene Anträge,
 * wer heute fehlt und was die Lohnbuchhaltung wissen muss, sieht, wer
 * entscheidet. Die Konten aller sieht, wer entscheidet oder Verträge pflegt.
 * Was jemand nicht sehen darf, liefert der Server gar nicht erst.
 */
@Component({
  selector: 'souffleur-abwesenheiten',
  imports: [DatePipe, FormsModule, DialogComponent, MeldungComponent],
  templateUrl: './abwesenheiten.component.html',
})
export class AbwesenheitenComponent implements OnInit {
  private readonly absences = inject(AbsencesService);
  private readonly auth = inject(AuthService);
  private readonly peopleList = inject(PeopleService);

  protected readonly asDays = asDays;
  protected readonly signedHours = signedHours;
  protected readonly weekdays = WEEKDAYS;
  protected readonly Status = AbsenceStatus;

  /** Vor allem anderen: Der leere Entwurf rechnet damit. */
  protected readonly today = isoDay(new Date());

  protected readonly loading = signal(true);
  protected readonly busy = signal(false);
  protected readonly problems = signal<string[]>([]);

  protected readonly year = signal(new Date().getFullYear());
  protected readonly me = signal<{ contactId: string | null; name: string | null } | null>(null);
  protected readonly kinds = signal<AbsenceKindInfo[]>([]);
  protected readonly people = signal<Person[]>([]);

  protected readonly mine = signal<Absence[]>([]);
  protected readonly balance = signal<Balance | null>(null);

  protected readonly openRequests = signal<Absence[]>([]);
  protected readonly away = signal<Absence[]>([]);
  protected readonly payroll = signal<Absence[]>([]);
  protected readonly team = signal<Balance[]>([]);

  protected readonly open = signal<Open | null>(null);
  protected readonly draft = signal<Draft>(this.emptyDraft());
  protected readonly reason = signal('');

  /** Für den Vertragsbogen: die Stände der Person und die Gutschriften des Jahres. */
  protected readonly employments = signal<Employment[]>([]);
  protected readonly adjustments = signal<Adjustment[]>([]);
  protected readonly employmentDraft = signal<EmploymentDraft>(emptyEmployment());
  protected readonly adjustmentDraft = signal({ days: 1, reason: '' });

  protected readonly mayDecide = computed(() => this.auth.can('PersonnelApprove'));
  protected readonly mayKeepRecords = computed(() => this.auth.can('PersonnelRecords'));
  protected readonly maySeeTeam = computed(() => this.mayDecide() || this.mayKeepRecords());

  /** Was beantragt wird. Krankheit hat ihren eigenen Bogen. */
  protected readonly requestKinds = computed(() => this.kinds().filter((k) => !k.isSickness));
  protected readonly sickKinds = computed(() => this.kinds().filter((k) => k.isSickness));

  async ngOnInit(): Promise<void> {
    try {
      const [me, kinds] = await Promise.all([this.absences.me(), this.absences.kinds()]);
      this.me.set(me);
      this.kinds.set(kinds);

      // Alle brauchen die Liste: für die Vertretung im eigenen Antrag.
      this.people.set(await this.peopleList.load());

      await this.reload();
    } catch (error) {
      this.problems.set(messagesFrom(error));
    } finally {
      this.loading.set(false);
    }
  }

  protected async reload(): Promise<void> {
    const year = this.year();
    const from = `${year}-01-01`;
    const to = `${year}-12-31`;

    if (this.me()?.contactId) {
      const [mine, balance] = await Promise.all([
        this.absences.mine(from, to),
        this.absences.balance(year),
      ]);
      this.mine.set(mine);
      this.balance.set(balance);
    }

    if (this.mayDecide()) {
      const [open, away, payroll] = await Promise.all([
        this.absences.open(),
        this.absences.all(this.today, this.today),
        this.absences.payroll(),
      ]);
      this.openRequests.set(open);
      this.away.set(
        away.filter(
          (a) => a.status === AbsenceStatus.Approved || a.status === AbsenceStatus.Reported,
        ),
      );
      this.payroll.set(payroll);
    }

    if (this.maySeeTeam()) {
      this.team.set(await this.absences.team(year));
    }
  }

  protected async shiftYear(by: number): Promise<void> {
    this.year.update((y) => y + by);
    await this.reload();
  }

  // --- Beantragen und melden ---

  protected startRequest(): void {
    this.draft.set(this.emptyDraft());
    this.problems.set([]);
    this.open.set({ what: 'antrag' });
  }

  protected startSick(): void {
    this.draft.set({
      ...this.emptyDraft(),
      kind: this.sickKinds()[0]?.value ?? 7,
      startDate: this.today,
      endDate: this.today,
    });
    this.problems.set([]);
    this.open.set({ what: 'krank' });
  }

  protected patch(change: Partial<Draft>): void {
    this.draft.update((current) => {
      const next = { ...current, ...change };

      // Das Ende folgt dem Anfang, solange es davor läge -- sonst tippt man
      // jeden Tag zweimal.
      if (change.startDate && next.endDate < change.startDate) {
        next.endDate = change.startDate;
      }

      if (next.startDate !== next.endDate) {
        next.isHalfDay = false;
      }

      return next;
    });
  }

  protected async submit(): Promise<void> {
    const d = this.draft();
    this.busy.set(true);
    this.problems.set([]);

    try {
      await this.absences.request({
        contactId: d.contactId || null,
        kind: Number(d.kind),
        startDate: d.startDate,
        endDate: d.endDate,
        isHalfDay: d.isHalfDay,
        deputyId: d.deputyId || null,
        note: d.note || null,
        certificateReceived: d.certificateReceived,
      });
      this.open.set(null);
      await this.reload();
    } catch (error) {
      this.problems.set(messagesFrom(error));
    } finally {
      this.busy.set(false);
    }
  }

  // --- Entscheiden ---

  protected async approve(absence: Absence): Promise<void> {
    await this.run(() => this.absences.decide(absence.id, true, null));
  }

  protected startReject(absence: Absence): void {
    this.reason.set('');
    this.problems.set([]);
    this.open.set({ what: 'ablehnen', absence });
  }

  protected async reject(): Promise<void> {
    const open = this.open();
    if (open?.what !== 'ablehnen') {
      return;
    }

    await this.run(() => this.absences.decide(open.absence.id, false, this.reason()), true);
  }

  /** Ein offener Antrag geht ohne Rückfrage; was schon gilt, braucht einen Grund. */
  protected async withdraw(absence: Absence): Promise<void> {
    if (absence.status === AbsenceStatus.Requested) {
      await this.run(() => this.absences.withdraw(absence.id, null));
      return;
    }

    this.reason.set('');
    this.problems.set([]);
    this.open.set({ what: 'aufheben', absence });
  }

  protected async confirmWithdraw(): Promise<void> {
    const open = this.open();
    if (open?.what !== 'aufheben') {
      return;
    }

    await this.run(() => this.absences.withdraw(open.absence.id, this.reason()), true);
  }

  /** Ob die Person selbst noch zurückziehen kann -- sonst geht es über die Leitung. */
  protected mayWithdraw(absence: Absence): boolean {
    if (this.mayDecide()) {
      return (
        absence.status !== AbsenceStatus.Rejected && absence.status !== AbsenceStatus.Withdrawn
      );
    }

    return (
      absence.status === AbsenceStatus.Requested ||
      absence.status === AbsenceStatus.Reported ||
      (absence.status === AbsenceStatus.Approved && absence.startDate > this.today)
    );
  }

  protected startUpdateSickness(absence: Absence): void {
    this.draft.set({
      ...this.emptyDraft(),
      startDate: absence.startDate,
      endDate: absence.endDate,
      certificateReceived: absence.certificateReceived,
    });
    this.problems.set([]);
    this.open.set({ what: 'nachfuehren', absence });
  }

  protected async updateSickness(): Promise<void> {
    const open = this.open();
    if (open?.what !== 'nachfuehren') {
      return;
    }

    const d = this.draft();
    await this.run(
      () => this.absences.updateSickness(open.absence.id, d.endDate, d.certificateReceived),
      true,
    );
  }

  protected async markPayroll(absence: Absence): Promise<void> {
    await this.run(() => this.absences.markPayroll(absence.id, true));
  }

  // --- Verträge und Konten ---

  protected async startContract(
    person: Balance | { contactId: string; name: string },
  ): Promise<void> {
    this.problems.set([]);
    this.employmentDraft.set(emptyEmployment());
    this.adjustmentDraft.set({ days: 1, reason: '' });
    this.open.set({ what: 'vertrag', person });
    await this.loadContract(person.contactId);
  }

  /** Eine Person aus dem Adressbuch, die noch keinen Vertrag im System hat. */
  protected readonly newPerson = signal('');

  protected readonly withoutContract = computed(() => {
    const known = new Set(this.team().map((b) => b.contactId));
    return this.people().filter((p) => !known.has(p.id));
  });

  protected async addPerson(): Promise<void> {
    const person = this.people().find((p) => p.id === this.newPerson());
    if (person) {
      this.newPerson.set('');
      await this.startContract({ contactId: person.id, name: person.name });
    }
  }

  private async loadContract(contactId: string): Promise<void> {
    const [employments, adjustments] = await Promise.all([
      this.absences.employments(contactId),
      this.absences.adjustments(contactId, this.year()),
    ]);
    this.employments.set(employments);
    this.adjustments.set(adjustments);
  }

  protected patchEmployment(change: Partial<EmploymentDraft>): void {
    this.employmentDraft.update((current) => ({ ...current, ...change }));
  }

  /** Bitweise geht im Template nicht -- deshalb hier. */
  protected hasDay(bit: number): boolean {
    return (this.employmentDraft().workDays & bit) !== 0;
  }

  protected toggleDay(bit: number): void {
    this.employmentDraft.update((current) => ({ ...current, workDays: current.workDays ^ bit }));
  }

  protected editEmployment(employment: Employment): void {
    this.employmentDraft.set({
      id: employment.id,
      validFrom: employment.validFrom,
      endsOn: employment.endsOn ?? '',
      weeklyHours: employment.weeklyHours,
      workDays: employment.workDays,
      annualLeaveDays: employment.annualLeaveDays,
      note: employment.note ?? '',
    });
  }

  protected async saveEmployment(): Promise<void> {
    const open = this.open();
    if (open?.what !== 'vertrag') {
      return;
    }

    const d = this.employmentDraft();
    this.busy.set(true);
    this.problems.set([]);

    try {
      await this.absences.saveEmployment(d.id, {
        contactId: open.person.contactId,
        validFrom: d.validFrom,
        endsOn: d.endsOn || null,
        weeklyHours: Number(d.weeklyHours),
        workDays: d.workDays,
        annualLeaveDays: Number(d.annualLeaveDays),
        note: d.note || null,
      });
      this.employmentDraft.set(emptyEmployment());
      await this.loadContract(open.person.contactId);
      await this.reload();
    } catch (error) {
      this.problems.set(messagesFrom(error));
    } finally {
      this.busy.set(false);
    }
  }

  protected async removeEmployment(employment: Employment): Promise<void> {
    const open = this.open();
    if (open?.what !== 'vertrag') {
      return;
    }

    await this.absences.removeEmployment(employment.id);
    await this.loadContract(open.person.contactId);
    await this.reload();
  }

  protected async addAdjustment(): Promise<void> {
    const open = this.open();
    if (open?.what !== 'vertrag') {
      return;
    }

    const d = this.adjustmentDraft();
    this.busy.set(true);
    this.problems.set([]);

    try {
      await this.absences.addAdjustment(
        open.person.contactId,
        this.year(),
        Number(d.days),
        d.reason,
      );
      this.adjustmentDraft.set({ days: 1, reason: '' });
      await this.loadContract(open.person.contactId);
      await this.reload();
    } catch (error) {
      this.problems.set(messagesFrom(error));
    } finally {
      this.busy.set(false);
    }
  }

  protected async removeAdjustment(adjustment: Adjustment): Promise<void> {
    const open = this.open();
    if (open?.what !== 'vertrag') {
      return;
    }

    await this.absences.removeAdjustment(adjustment.id);
    await this.loadContract(open.person.contactId);
    await this.reload();
  }

  protected dayNames(bits: number): string {
    return this.weekdays
      .filter((d) => (bits & d.bit) !== 0)
      .map((d) => d.short)
      .join(' ');
  }

  protected close(): void {
    this.open.set(null);
    this.problems.set([]);
  }

  /** Ein Zeitraum, wie er in Listen steht. */
  protected span(absence: Absence): string {
    const from = new Date(absence.startDate + 'T00:00:00');
    const to = new Date(absence.endDate + 'T00:00:00');
    const short = (d: Date) => d.toLocaleDateString('de-DE', { day: '2-digit', month: '2-digit' });
    const long = (d: Date) =>
      d.toLocaleDateString('de-DE', { day: '2-digit', month: '2-digit', year: 'numeric' });

    if (absence.startDate === absence.endDate) {
      return absence.isHalfDay ? `${long(from)}, halber Tag` : long(from);
    }

    return `${short(from)}–${long(to)}`;
  }

  private async run(action: () => Promise<unknown>, closeAfter = false): Promise<void> {
    this.busy.set(true);
    this.problems.set([]);

    try {
      await action();
      if (closeAfter) {
        this.open.set(null);
      }
      await this.reload();
    } catch (error) {
      this.problems.set(messagesFrom(error));
    } finally {
      this.busy.set(false);
    }
  }

  private emptyDraft(): Draft {
    return {
      contactId: '',
      kind: 1,
      startDate: this.today,
      endDate: this.today,
      isHalfDay: false,
      deputyId: '',
      note: '',
      certificateReceived: false,
    };
  }
}

interface EmploymentDraft {
  id: string | null;
  validFrom: string;
  endsOn: string;
  weeklyHours: number;
  workDays: number;
  annualLeaveDays: number;
  note: string;
}

function emptyEmployment(): EmploymentDraft {
  return {
    id: null,
    validFrom: `${new Date().getFullYear()}-01-01`,
    endsOn: '',
    weeklyHours: 40,
    workDays: 31,
    annualLeaveDays: 30,
    note: '',
  };
}
