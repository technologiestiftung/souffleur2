import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

/** Die Arten, wie der Server sie nummeriert. */
export const AbsenceKind = {
  Vacation: 1,
  TimeOffInLieu: 2,
  EducationLeave: 3,
  SpecialLeave: 4,
  Unpaid: 5,
  ParentalLeave: 6,
  Sick: 7,
  ChildSick: 8,
} as const;

export const AbsenceStatus = {
  Requested: 1,
  Approved: 2,
  Rejected: 3,
  Withdrawn: 4,
  Reported: 5,
} as const;

export interface AbsenceKindInfo {
  value: number;
  label: string;
  needsApproval: boolean;
  isSickness: boolean;
  countsAgainstLeave: boolean;
  reducesHours: boolean;
}

export interface Absence {
  id: string;
  contactId: string;
  contactName: string | null;
  kind: number;
  kindLabel: string;
  isSickness: boolean;
  concernsPayroll: boolean;
  status: number;
  statusLabel: string;
  startDate: string;
  endDate: string;
  isHalfDay: boolean;

  /** Arbeitstage, die es kostet -- Wochenenden und Feiertage zählen nicht. */
  days: number;
  deputyId: string | null;
  deputyName: string | null;
  note: string | null;
  certificateReceived: boolean;
  decidedAt: string | null;
  decisionNote: string | null;
  payrollReportedAt: string | null;
  createdAt: string;
}

export interface AbsenceInput {
  contactId?: string | null;
  kind: number;
  startDate: string;
  endDate: string;
  isHalfDay: boolean;
  deputyId?: string | null;
  note?: string | null;
  certificateReceived?: boolean;
}

export interface Employment {
  id: string;
  contactId: string;
  validFrom: string;
  endsOn: string | null;
  weeklyHours: number;

  /** Bitfeld: Montag 1, Dienstag 2 … Sonntag 64. */
  workDays: number;
  annualLeaveDays: number;
  dailyTargetMinutes: number;
  note: string | null;
}

export type EmploymentInput = Omit<Employment, 'id' | 'dailyTargetMinutes'>;

export interface Hours {
  from: string;
  to: string;
  target: number;
  credited: number;
  worked: number;
  balance: number;
  missingDays: string[];
}

export interface Balance {
  contactId: string;
  name: string;
  employment: Employment | null;
  year: number;
  entitlement: number;
  adjustments: number;
  taken: number;
  pending: number;
  remaining: number;
  hours: Hours | null;
}

export interface Adjustment {
  id: string;
  contactId: string;
  year: number;
  days: number;
  reason: string;
  createdAt: string;
}

export interface Holiday {
  date: string;
  name: string;
  isHouseDay: boolean;
}

export interface HouseHoliday {
  id: string;
  month: number;
  day: number;
  name: string;
}

/** Die Wochentage als Bits, in der Reihenfolge, in der sie im Bogen stehen. */
export const WEEKDAYS = [
  { bit: 1, short: 'Mo' },
  { bit: 2, short: 'Di' },
  { bit: 4, short: 'Mi' },
  { bit: 8, short: 'Do' },
  { bit: 16, short: 'Fr' },
  { bit: 32, short: 'Sa' },
  { bit: 64, short: 'So' },
] as const;

/**
 * Urlaub, Krankheit und was sonst fehlen lässt -- dazu Vertragsdaten und
 * Konten.
 *
 * Krankmeldungen sind Gesundheitsdaten. Was die Oberfläche davon zeigt,
 * entscheidet der Server: Wer kein Recht hat, bekommt fremde gar nicht erst.
 */
@Injectable({ providedIn: 'root' })
export class AbsencesService {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/personnel/absences';

  kinds(): Promise<AbsenceKindInfo[]> {
    return firstValueFrom(this.http.get<AbsenceKindInfo[]>(`${this.base}/kinds`));
  }

  me(): Promise<{ contactId: string | null; name: string | null }> {
    return firstValueFrom(
      this.http.get<{ contactId: string | null; name: string | null }>(`${this.base}/me`),
    );
  }

  mine(from: string, to: string): Promise<Absence[]> {
    return firstValueFrom(this.http.get<Absence[]>(`${this.base}?from=${from}&to=${to}`));
  }

  all(from: string, to: string): Promise<Absence[]> {
    return firstValueFrom(this.http.get<Absence[]>(`${this.base}?from=${from}&to=${to}&all=true`));
  }

  open(): Promise<Absence[]> {
    return firstValueFrom(this.http.get<Absence[]>(`${this.base}/open`));
  }

  payroll(): Promise<Absence[]> {
    return firstValueFrom(this.http.get<Absence[]>(`${this.base}/payroll`));
  }

  request(input: AbsenceInput): Promise<Absence> {
    return firstValueFrom(this.http.post<Absence>(this.base, input));
  }

  decide(id: string, approve: boolean, note: string | null): Promise<Absence> {
    return firstValueFrom(this.http.post<Absence>(`${this.base}/${id}/decide`, { approve, note }));
  }

  withdraw(id: string, reason: string | null): Promise<Absence> {
    return firstValueFrom(this.http.post<Absence>(`${this.base}/${id}/withdraw`, { reason }));
  }

  updateSickness(id: string, endDate: string, certificateReceived: boolean): Promise<Absence> {
    return firstValueFrom(
      this.http.put<Absence>(`${this.base}/${id}/sickness`, { endDate, certificateReceived }),
    );
  }

  markPayroll(id: string, reported: boolean): Promise<Absence> {
    return firstValueFrom(this.http.put<Absence>(`${this.base}/${id}/payroll`, { reported }));
  }

  balance(year: number, contactId?: string): Promise<Balance> {
    const person = contactId ? `&contactId=${contactId}` : '';
    return firstValueFrom(this.http.get<Balance>(`${this.base}/balance?year=${year}${person}`));
  }

  team(year: number): Promise<Balance[]> {
    return firstValueFrom(this.http.get<Balance[]>(`${this.base}/team?year=${year}`));
  }

  holidays(year: number): Promise<Holiday[]> {
    return firstValueFrom(this.http.get<Holiday[]>(`${this.base}/holidays?year=${year}`));
  }

  employments(contactId: string): Promise<Employment[]> {
    return firstValueFrom(this.http.get<Employment[]>(`${this.base}/employments/${contactId}`));
  }

  saveEmployment(id: string | null, input: EmploymentInput): Promise<Employment> {
    return firstValueFrom(
      id
        ? this.http.put<Employment>(`${this.base}/employments/${id}`, input)
        : this.http.post<Employment>(`${this.base}/employments`, input),
    );
  }

  removeEmployment(id: string): Promise<void> {
    return firstValueFrom(this.http.delete<void>(`${this.base}/employments/${id}`));
  }

  adjustments(contactId: string, year: number): Promise<Adjustment[]> {
    return firstValueFrom(
      this.http.get<Adjustment[]>(`${this.base}/adjustments/${contactId}?year=${year}`),
    );
  }

  addAdjustment(
    contactId: string,
    year: number,
    days: number,
    reason: string,
  ): Promise<Adjustment> {
    return firstValueFrom(
      this.http.post<Adjustment>(`${this.base}/adjustments`, { contactId, year, days, reason }),
    );
  }

  removeAdjustment(id: string): Promise<void> {
    return firstValueFrom(this.http.delete<void>(`${this.base}/adjustments/${id}`));
  }

  houseHolidays(): Promise<HouseHoliday[]> {
    return firstValueFrom(this.http.get<HouseHoliday[]>(`${this.base}/house-holidays`));
  }

  addHouseHoliday(month: number, day: number, name: string): Promise<HouseHoliday> {
    return firstValueFrom(
      this.http.post<HouseHoliday>(`${this.base}/house-holidays`, { month, day, name }),
    );
  }

  removeHouseHoliday(id: string): Promise<void> {
    return firstValueFrom(this.http.delete<void>(`${this.base}/house-holidays/${id}`));
  }
}

/** Ein Tag als JJJJ-MM-TT in Ortszeit -- nicht über toISOString, das rechnet in UTC. */
export function isoDay(date: Date): string {
  const month = String(date.getMonth() + 1).padStart(2, '0');
  const day = String(date.getDate()).padStart(2, '0');
  return `${date.getFullYear()}-${month}-${day}`;
}

/** Minuten als „+3:15 Std.“ mit Vorzeichen -- für Salden. */
export function signedHours(minutes: number): string {
  const sign = minutes > 0 ? '+' : minutes < 0 ? '−' : '±';
  const abs = Math.abs(minutes);
  return `${sign}${Math.floor(abs / 60)}:${String(abs % 60).padStart(2, '0')} Std.`;
}

/** Tage mit deutschem Komma, halbe Tage als ,5. */
export function asDays(days: number): string {
  return days.toLocaleString('de-DE', { maximumFractionDigits: 1 });
}
