import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';

export interface CatalogInfo {
  catalog: number;
  key: string;
  label: string;
  description: string;
  allowsHierarchy: boolean;
  usesColor: boolean;
}

export interface CatalogEntry {
  id: string;
  catalog: number;
  key: string;
  label: string;
  code: string | null;
  description: string | null;
  color: string | null;
  sortOrder: number;
  isActive: boolean;
  isSystem: boolean;
  parentId: string | null;
  usageCount: number;
  canBeDeleted: boolean;
}

export interface CatalogEntryInput {
  label: string;
  key?: string | null;
  code?: string | null;
  description?: string | null;
  color?: string | null;
  sortOrder?: number | null;
  isActive?: boolean | null;
  parentId?: string | null;
}

/** Die Listen des Hauses -- siehe ADR-0005. */
@Injectable({ providedIn: 'root' })
export class CatalogsService {
  private readonly http = inject(HttpClient);

  private readonly cached = new Map<string, CatalogEntry[]>();
  private readonly catalogList = signal<CatalogInfo[]>([]);

  readonly catalogs = this.catalogList.asReadonly();

  async loadCatalogs(): Promise<CatalogInfo[]> {
    const found = await firstValueFrom(this.http.get<CatalogInfo[]>('/api/catalogs'));
    this.catalogList.set(found);
    return found;
  }

  async entries(catalog: string, includeInactive = false): Promise<CatalogEntry[]> {
    const key = `${catalog}:${includeInactive}`;
    const cached = this.cached.get(key);
    if (cached) {
      return cached;
    }

    const found = await firstValueFrom(
      this.http.get<CatalogEntry[]>(
        `/api/catalogs/${catalog}${includeInactive ? '?includeInactive=true' : ''}`,
      ),
    );
    this.cached.set(key, found);
    return found;
  }

  async create(catalog: string, input: CatalogEntryInput): Promise<CatalogEntry> {
    const created = await firstValueFrom(
      this.http.post<CatalogEntry>(`/api/catalogs/${catalog}`, input),
    );
    this.forget(catalog);
    return created;
  }

  async update(catalog: string, id: string, input: CatalogEntryInput): Promise<CatalogEntry> {
    const updated = await firstValueFrom(
      this.http.put<CatalogEntry>(`/api/catalogs/${catalog}/${id}`, input),
    );
    this.forget(catalog);
    return updated;
  }

  async setActive(catalog: string, id: string, isActive: boolean): Promise<void> {
    await firstValueFrom(
      this.http.post(`/api/catalogs/${catalog}/${id}/active`, { isActive }),
    );
    this.forget(catalog);
  }

  async remove(catalog: string, id: string): Promise<void> {
    await firstValueFrom(this.http.delete(`/api/catalogs/${catalog}/${id}`));
    this.forget(catalog);
  }

  async reorder(catalog: string, order: string[]): Promise<void> {
    await firstValueFrom(this.http.post(`/api/catalogs/${catalog}/order`, order));
    this.forget(catalog);
  }

  /** Nach jeder Änderung neu holen -- sonst zeigt die Auswahl den alten Stand. */
  private forget(catalog: string): void {
    for (const key of [...this.cached.keys()]) {
      if (key.startsWith(`${catalog}:`)) {
        this.cached.delete(key);
      }
    }
  }
}
