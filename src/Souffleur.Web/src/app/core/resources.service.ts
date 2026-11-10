import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';

export interface Resource {
  id: string;
  name: string;
  shortName: string | null;
  display: string;
  kindId: string | null;
  kindLabel: string | null;
  kindColor: string | null;
  description: string | null;
  capacity: number | null;
  isExclusive: boolean;
  sortOrder: number;
  isActive: boolean;
  usageCount: number;
  canBeDeleted: boolean;
}

export interface ResourceInput {
  name: string;
  shortName?: string | null;
  kindId?: string | null;
  description?: string | null;
  capacity?: number | null;
  isExclusive?: boolean;
  sortOrder?: number | null;
  isActive?: boolean | null;
}

/**
 * Die Ressourcen des Hauses -- Bühnen, Räume, Werkstätten, Fahrzeuge.
 *
 * Eine eigene Liste und kein Katalog: Von der Ressource hängt ab, ob zwei
 * Termine nebeneinander dürfen. Siehe ADR-0006.
 */
@Injectable({ providedIn: 'root' })
export class ResourcesService {
  private readonly http = inject(HttpClient);

  private readonly loaded = signal<Resource[]>([]);

  readonly resources = this.loaded.asReadonly();

  async load(includeInactive = false): Promise<Resource[]> {
    const found = await firstValueFrom(
      this.http.get<Resource[]>(
        `/api/resources${includeInactive ? '?includeInactive=true' : ''}`,
      ),
    );
    this.loaded.set(found);
    return found;
  }

  async create(input: ResourceInput): Promise<Resource> {
    const created = await firstValueFrom(this.http.post<Resource>('/api/resources', input));
    return created;
  }

  async update(id: string, input: ResourceInput): Promise<Resource> {
    return firstValueFrom(this.http.put<Resource>(`/api/resources/${id}`, input));
  }

  async setActive(id: string, isActive: boolean): Promise<void> {
    await firstValueFrom(this.http.post(`/api/resources/${id}/active`, { isActive }));
  }

  async remove(id: string): Promise<void> {
    await firstValueFrom(this.http.delete(`/api/resources/${id}`));
  }

  async reorder(order: string[]): Promise<void> {
    await firstValueFrom(this.http.post('/api/resources/order', order));
  }
}
