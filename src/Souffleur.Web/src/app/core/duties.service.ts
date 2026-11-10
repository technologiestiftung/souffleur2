import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

/** Eine Funktion an einem Termin: gebraucht, eingeteilt, offen. */
export interface DutySlot {
  functionId: string;
  function: string;
  required: number;
  assigned: number;
  open: number;

  /** Aus welcher Gruppe zuerst vorgeschlagen wird. */
  groupId: string | null;
  groupName: string | null;
}

export interface DutyPerson {
  contactId: string;
  name: string;
  function: string | null;
  statusLabel: string;
  declined: boolean;
}

export interface PlannedDuty {
  eventId: string | null;
  startsAt: string;
  localDate: string;
  title: string;
  location: string | null;
  categoryId: string | null;
  categoryLabel: string | null;
  slots: DutySlot[];
  assigned: DutyPerson[];
  open: number;
}

export interface DutyRequirement {
  functionId: string;
  function: string;
  count: number;
  groupId: string | null;
  groupName: string | null;
}

/** Der Bedarf einer Terminart. */
export interface DutyTemplate {
  categoryId: string;
  categoryLabel: string;
  note: string | null;
  requirements: DutyRequirement[];
}

/**
 * Der Dienstplan.
 *
 * Der Bedarf hängt an der Terminkategorie: Dienste entstehen im Theater an
 * Vorstellungen und Proben, und die stehen schon im Kalender. Der Gewinn daran
 * ist die Lücke — eine Vorstellung ohne jede Einteilung taucht auf, weil ihre
 * Terminart einen Bedarf hat.
 */
@Injectable({ providedIn: 'root' })
export class DutiesService {
  private readonly http = inject(HttpClient);

  plan(from: string, to: string): Promise<PlannedDuty[]> {
    return firstValueFrom(
      this.http.get<PlannedDuty[]>(`/api/personnel/plan?from=${from}&to=${to}`),
    );
  }

  templates(): Promise<DutyTemplate[]> {
    return firstValueFrom(this.http.get<DutyTemplate[]>('/api/personnel/duty-templates'));
  }

  save(
    categoryId: string,
    requirements: { functionId: string; count: number; groupId: string | null }[],
    note: string | null,
  ): Promise<unknown> {
    return firstValueFrom(
      this.http.put(`/api/personnel/duty-templates/${categoryId}`, { requirements, note }),
    );
  }

  remove(categoryId: string): Promise<void> {
    return firstValueFrom(this.http.delete<void>(`/api/personnel/duty-templates/${categoryId}`));
  }
}
