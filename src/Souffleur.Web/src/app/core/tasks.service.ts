import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';

export interface TaskItem {
  id: string;
  title: string;
  description: string | null;
  categoryId: string | null;
  categoryLabel: string | null;
  categoryColor: string | null;
  assigneeId: string | null;
  assigneeName: string | null;

  /** Die zuständige Gruppe -- neben der Person, nicht statt ihr. */
  groupId: string | null;
  productionId: string | null;
  productionTitle: string | null;
  groupName: string | null;

  /** Woran die Aufgabe in der Buchhaltung hängt -- eine Verknüpfung, keine Buchung. */
  costCenterId: string | null;
  costCenterLabel: string | null;

  /** Beteiligte: arbeiten mit, ohne die Aufgabe zu tragen. */
  participants: TaskContact[];

  /** Verknüpfte Kontakte: kommen darin vor, tun aber nichts. */
  contacts: TaskContact[];
  state: number;
  stateLabel: string;
  isOpen: boolean;
  isOverdue: boolean;
  dueDate: string | null;
  startDate: string | null;
  parentId: string | null;
  sortOrder: number;
  closedAt: string | null;

  /** Dokumente und Lesezeichen zusammen -- die Büroklammer in der Zeile. */
  attachmentCount: number;
}

export interface TaskContact {
  id: string;
  name: string;
}

export interface TaskInput {
  title: string;
  description?: string | null;
  categoryId?: string | null;
  assigneeId?: string | null;
  groupId?: string | null;
  productionId?: string | null;
  costCenterId?: string | null;
  participantIds?: string[];
  contactIds?: string[];
  dueDate?: string | null;
  startDate?: string | null;
  parentId?: string | null;
  state?: number;
}

/** Ein Balken auf dem Zeitstrahl. */
export interface TimelineEntry {
  id: string;
  title: string;
  from: string;
  to: string;
  isMilestone: boolean;
  categoryId: string | null;
  categoryLabel: string | null;
  categoryColor: string | null;
  assigneeName: string | null;
  state: number;
  stateLabel: string;
  isOpen: boolean;
  isOverdue: boolean;
  parentId: string | null;
}

export interface Dependency {
  taskId: string;
  dependsOnId: string;
}

/** Eine Reihenfolge, die nicht mehr aufgeht. */
export interface Breach {
  taskId: string;
  taskTitle: string;
  dependsOnId: string;
  dependsOnTitle: string;
}

export interface Timeline {
  from: string;
  to: string;
  tasks: TimelineEntry[];
  dependencies: Dependency[];
  breaches: Breach[];
  unscheduled: number;
}

export interface TaskStateInfo {
  state: number;
  key: string;
  label: string;
  isOpen: boolean;
}

export const TaskState = {
  Open: 1,
  InProgress: 2,
  Waiting: 3,
  Done: 4,
  Dropped: 5,
} as const;

/** Die Aufgaben des Hauses -- siehe ADR-0007. */
@Injectable({ providedIn: 'root' })
export class TasksService {
  private readonly http = inject(HttpClient);

  private readonly loaded = signal<TaskItem[]>([]);
  private readonly busy = signal(false);

  readonly tasks = this.loaded.asReadonly();
  readonly loading = this.busy.asReadonly();

  async load(
    filter: {
      states?: number[];
      category?: string | null;
      assignee?: string | null;
      includeClosed?: boolean;

      /** Nur was an einer Gruppe hängt, in der ich bin. */
      meineGruppen?: boolean;
    } = {},
  ): Promise<TaskItem[]> {
    this.busy.set(true);

    try {
      let params = new HttpParams();
      for (const state of filter.states ?? []) {
        params = params.append('states', state);
      }
      if (filter.category) {
        params = params.set('category', filter.category);
      }
      if (filter.assignee) {
        params = params.set('assignee', filter.assignee);
      }
      if (filter.includeClosed) {
        params = params.set('includeClosed', true);
      }
      if (filter.meineGruppen) {
        params = params.set('meineGruppen', true);
      }

      const found = await firstValueFrom(this.http.get<TaskItem[]>('/api/tasks', { params }));
      this.loaded.set(found);
      return found;
    } catch {
      // Modul aus oder nicht angemeldet: dann gibt es nichts zu zeigen.
      this.loaded.set([]);
      return [];
    } finally {
      this.busy.set(false);
    }
  }

  /**
   * Alle Aufgaben, auch die geschlossenen -- und ohne die gefilterte Liste
   * der Aufgabenseite zu überschreiben.
   */
  snapshot(): Promise<TaskItem[]> {
    const params = new HttpParams().set('includeClosed', true);
    return firstValueFrom(this.http.get<TaskItem[]>('/api/tasks', { params }));
  }

  timeline(from: string, to: string, includeClosed = false): Promise<Timeline> {
    let params = new HttpParams().set('from', from).set('to', to);
    if (includeClosed) {
      params = params.set('includeClosed', true);
    }
    return firstValueFrom(this.http.get<Timeline>('/api/tasks/timeline', { params }));
  }

  async link(taskId: string, dependsOnId: string): Promise<void> {
    await firstValueFrom(this.http.post(`/api/tasks/${taskId}/depends-on/${dependsOnId}`, {}));
  }

  async unlink(taskId: string, dependsOnId: string): Promise<void> {
    await firstValueFrom(this.http.delete(`/api/tasks/${taskId}/depends-on/${dependsOnId}`));
  }

  states(): Promise<TaskStateInfo[]> {
    return firstValueFrom(this.http.get<TaskStateInfo[]>('/api/tasks/states'));
  }

  create(input: TaskInput): Promise<TaskItem> {
    return firstValueFrom(this.http.post<TaskItem>('/api/tasks', input));
  }

  update(id: string, input: TaskInput): Promise<TaskItem> {
    return firstValueFrom(this.http.put<TaskItem>(`/api/tasks/${id}`, input));
  }

  setState(id: string, state: number): Promise<TaskItem> {
    return firstValueFrom(this.http.post<TaskItem>(`/api/tasks/${id}/state`, { state }));
  }

  async remove(id: string): Promise<void> {
    await firstValueFrom(this.http.delete(`/api/tasks/${id}`));
  }
}
