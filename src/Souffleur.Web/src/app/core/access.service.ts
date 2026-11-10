import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

/** Ein Recht aus dem Katalog. */
export interface PermissionInfo {
  permission: number;
  key: string;
  label: string;
  area: string;
}

export interface AccessRole {
  id: string;

  /** Der Schlüssel. Bei mitgelieferten Rollen englisch und unveränderlich. */
  name: string;

  /** Was angezeigt wird -- bei mitgelieferten Rollen deutsch. */
  label: string;
  description: string | null;

  /** Mitgeliefert: änderbar, aber nicht löschbar. */
  isBuiltIn: boolean;
  permissions: number[];
}

export interface AccessGroup {
  id: string;
  name: string;
  note: string | null;
  isActive: boolean;
  roleIds: string[];
  memberIds: string[];
}

export interface AccessUser {
  id: string;
  name: string;
  email: string | null;
  isActive: boolean;

  /** Nur die unmittelbar vergebenen -- die aus Gruppen stehen bei den Gruppen. */
  roleIds: string[];
}

export interface RoleInput {
  name: string;
  description: string | null;
  permissions: number[];
}

export interface GroupInput {
  name: string;
  note: string | null;
  isActive: boolean;
  roleIds: string[];
  memberIds: string[];
}

/**
 * Rollen, Gruppen und wer wozu gehört. Alles hier verlangt das Recht,
 * Zugänge zu verwalten -- ohne gibt es 403.
 */
@Injectable({ providedIn: 'root' })
export class AccessService {
  private readonly http = inject(HttpClient);

  permissions(): Promise<PermissionInfo[]> {
    return firstValueFrom(this.http.get<PermissionInfo[]>('/api/access/permissions'));
  }

  roles(): Promise<AccessRole[]> {
    return firstValueFrom(this.http.get<AccessRole[]>('/api/access/roles'));
  }

  async createRole(input: RoleInput): Promise<void> {
    await firstValueFrom(this.http.post('/api/access/roles', input));
  }

  async updateRole(id: string, input: RoleInput): Promise<void> {
    await firstValueFrom(this.http.put(`/api/access/roles/${id}`, input));
  }

  async deleteRole(id: string): Promise<void> {
    await firstValueFrom(this.http.delete(`/api/access/roles/${id}`));
  }

  groups(): Promise<AccessGroup[]> {
    return firstValueFrom(this.http.get<AccessGroup[]>('/api/access/groups'));
  }

  async createGroup(input: GroupInput): Promise<void> {
    await firstValueFrom(this.http.post('/api/access/groups', input));
  }

  async updateGroup(id: string, input: GroupInput): Promise<void> {
    await firstValueFrom(this.http.put(`/api/access/groups/${id}`, input));
  }

  async deleteGroup(id: string): Promise<void> {
    await firstValueFrom(this.http.delete(`/api/access/groups/${id}`));
  }

  users(): Promise<AccessUser[]> {
    return firstValueFrom(this.http.get<AccessUser[]>('/api/access/users'));
  }

  async setUserRoles(id: string, roleIds: string[]): Promise<void> {
    await firstValueFrom(this.http.put(`/api/access/users/${id}/roles`, { roleIds }));
  }
}
