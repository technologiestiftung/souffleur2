import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { AktivitaetsWoche } from '../shared/aktivitaet.component';

/** Wo eine Produktion steht -- dieselben Zahlen wie am Server. */
export const ProductionStatus = {
  Planned: 1,
  Rehearsing: 2,
  Running: 3,
  Archived: 4,
} as const;

export interface ProductionStatusInfo {
  status: number;
  key: string;
  label: string;
  isCurrent: boolean;
}

/** Was eine Produktion braucht -- dieselben Zahlen wie am Server. */
export const ProductionNeedKind = {
  Material: 1,
  Document: 2,
} as const;

/** Eine Rolle oder Funktion der Produktion; ohne Kontakt ist sie offen. */
export interface ProductionRole {
  id: string;
  name: string;
  functionId: string | null;
  functionLabel: string | null;
  contactId: string | null;
  contactName: string | null;
  isCast: boolean;
  note: string | null;
}

export interface ProductionBudgetLine {
  id: string;
  title: string;
  category: string | null;
  amount: number;
  note: string | null;
}

export interface ProductionNeed {
  id: string;
  kind: number;
  title: string;
  quantity: string | null;
  isDone: boolean;
  note: string | null;
}

/** Eine Besetzung aus Sicht des Kontakts. */
export interface Casting {
  productionId: string;
  productionTitle: string;
  status: number;
  statusLabel: string;
  isCurrent: boolean;
  roleId: string;
  roleName: string;
  functionLabel: string | null;
}

/**
 * Was an der Produktion hängt, gezählt. Kosten und Einnahmen fehlen, wenn
 * die Person die Finanzen nicht sehen darf; der Plan ist Vorgabe und bleibt.
 */
export interface ProductionFigures {
  roles: number;
  openRoles: number;
  openTasks: number;
  tasks: number;
  events: number;
  nextEvent: string | null;
  documents: number;
  planned: number;
  costs: number | null;
  income: number | null;
  tours: number;
  needs: number;
  openNeeds: number;
  papers: number;
  openPapers: number;
  /** Der Herzschlag: 26 Wochen, die letzte ist die laufende. */
  activity: AktivitaetsWoche[];
}

export interface Production {
  id: string;
  title: string;
  subtitle: string | null;
  status: number;
  statusLabel: string;
  isCurrent: boolean;
  premiereDate: string | null;
  closingDate: string | null;
  budget: number | null;
  description: string | null;
  research: string | null;
  notes: string | null;
  roles: ProductionRole[];
  budgetLines: ProductionBudgetLine[];
  needs: ProductionNeed[];
  figures: ProductionFigures | null;
}

export interface ProductionTask {
  id: string;
  title: string;
  state: number;
  stateLabel: string;
  isOpen: boolean;
  isOverdue: boolean;
  dueDate: string | null;
  assigneeName: string | null;
  groupName: string | null;
  categoryLabel: string | null;
  categoryColor: string | null;
}

export interface ProductionEvent {
  id: string;
  title: string;
  location: string | null;
  startsAt: string;
  endsAt: string;
  isAllDay: boolean;
  status: number;
  statusLabel: string;
  isException: boolean;
}

export interface ProductionSeries {
  id: string;
  title: string;
  location: string | null;
  startDate: string;
  until: string | null;
  rule: string;
  status: number;
}

export interface ProductionVoucher {
  id: string;
  label: string;
  fiscalYear: number;
  date: string;
  grossAmount: number;
  description: string;
  contactName: string | null;
  direction: number;
  isBooked: boolean;
  isCommitted: boolean;
}

export interface ProductionDocument {
  id: string;
  name: string;
  contentType: string;
  sizeInBytes: number;
  tags: string[];
}

export interface ProductionTour {
  id: string;
  title: string;
  startsOn: string | null;
  endsOn: string | null;
  stops: number;
  bindingStops: number;
  performances: number;
  confirmedFees: number;
}

/** Die Produktion mit allem, was die anderen Module an sie geheftet haben. */
export interface ProductionOverview {
  production: Production;
  tasks: ProductionTask[];
  events: ProductionEvent[];
  series: ProductionSeries[];
  vouchers: ProductionVoucher[] | null;
  documents: ProductionDocument[];
  tours: ProductionTour[];
}

export interface ProductionRoleInput {
  name: string;
  functionId?: string | null;
  contactId?: string | null;
  note?: string | null;
}

export interface ProductionBudgetLineInput {
  title: string;
  amount: number;
  category?: string | null;
  note?: string | null;
}

export interface ProductionNeedInput {
  title: string;
  kind: number;
  quantity?: string | null;
  isDone: boolean;
  note?: string | null;
}

/**
 * Der Bogen. Die Listen sind freiwillig: Fehlen sie, bleibt ihr Stand --
 * die Stammdaten lassen sich ändern, ohne die Besetzung mitzuschicken.
 */
export interface ProductionInput {
  title: string;
  subtitle?: string | null;
  status?: number | null;
  premiereDate?: string | null;
  closingDate?: string | null;
  budget?: number | null;
  description?: string | null;
  research?: string | null;
  notes?: string | null;
  roles?: ProductionRoleInput[] | null;
  budgetLines?: ProductionBudgetLineInput[] | null;
  needs?: ProductionNeedInput[] | null;
}

/**
 * Die Produktionen des Hauses.
 *
 * Die Produktion trägt ihre Vorgaben selbst -- Rollen, Budgetpositionen,
 * Bedarf, Papiere -- Lesezeichen hängen an ihr wie an jedem Gegenstand -- und
 * ist zugleich die Klammer: Aufgaben, Termine,
 * Belege und Dokumente tragen einen Verweis auf sie und werden in ihren
 * Modulen gepflegt. Dieser Dienst kennt beides.
 */
@Injectable({ providedIn: 'root' })
export class ProductionsService {
  private readonly http = inject(HttpClient);

  statuses(): Promise<ProductionStatusInfo[]> {
    return firstValueFrom(this.http.get<ProductionStatusInfo[]>('/api/productions/statuses'));
  }

  list(includeArchived = true): Promise<Production[]> {
    return firstValueFrom(
      this.http.get<Production[]>('/api/productions', { params: { includeArchived } }),
    );
  }

  /** Nur die laufenden, ohne Zahlen -- für Auswahlfelder in anderen Modulen. */
  async choices(): Promise<Production[]> {
    try {
      return await this.list(false);
    } catch {
      // Modul aus oder nicht erreichbar: Das Feld bleibt einfach leer.
      return [];
    }
  }

  overview(id: string): Promise<ProductionOverview> {
    return firstValueFrom(this.http.get<ProductionOverview>(`/api/productions/${id}`));
  }

  create(input: ProductionInput): Promise<Production> {
    return firstValueFrom(this.http.post<Production>('/api/productions', input));
  }

  update(id: string, input: ProductionInput): Promise<Production> {
    return firstValueFrom(this.http.put<Production>(`/api/productions/${id}`, input));
  }

  remove(id: string): Promise<void> {
    return firstValueFrom(this.http.delete<void>(`/api/productions/${id}`));
  }

  // Die Vorgaben abschnittsweise: Jeder Aufruf ersetzt genau seine Liste.

  saveRoles(id: string, roles: ProductionRoleInput[]): Promise<Production> {
    return firstValueFrom(this.http.put<Production>(`/api/productions/${id}/roles`, roles));
  }

  saveBudget(id: string, lines: ProductionBudgetLineInput[]): Promise<Production> {
    return firstValueFrom(this.http.put<Production>(`/api/productions/${id}/budget`, lines));
  }

  saveNeeds(id: string, needs: ProductionNeedInput[]): Promise<Production> {
    return firstValueFrom(this.http.put<Production>(`/api/productions/${id}/needs`, needs));
  }

  /** Vom Kontakt aus: eine Rolle dazu. */
  addRole(id: string, role: ProductionRoleInput): Promise<ProductionRole> {
    return firstValueFrom(this.http.post<ProductionRole>(`/api/productions/${id}/roles`, role));
  }

  removeRole(id: string, roleId: string): Promise<void> {
    return firstValueFrom(this.http.delete<void>(`/api/productions/${id}/roles/${roleId}`));
  }

  /** In welchen Produktionen jemand besetzt ist. Leer, wenn das Modul aus ist. */
  async castings(contactId: string): Promise<Casting[]> {
    try {
      return await firstValueFrom(
        this.http.get<Casting[]>(`/api/productions/castings/${contactId}`),
      );
    } catch {
      return [];
    }
  }

  assignVoucher(id: string, voucherId: string): Promise<void> {
    return firstValueFrom(this.http.put<void>(`/api/productions/${id}/vouchers/${voucherId}`, {}));
  }

  unassignVoucher(voucherId: string): Promise<void> {
    return firstValueFrom(this.http.delete<void>(`/api/productions/vouchers/${voucherId}`));
  }
}
