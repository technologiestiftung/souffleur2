import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { IconName } from '../shared/icon.component';

export interface ModuleInfo {
  /** Der Zahlenwert aus SouffleurModule -- damit adressiert die API sie. */
  module: number;
  key: string;
  label: string;
  isEnabled: boolean;
  canBeDisabled: boolean;
  note: string | null;
}

/**
 * Wohin ein Modul in der Oberfläche führt.
 *
 * Der Chat fehlt mit Absicht: Er lebt in der rechten Leiste, nicht als Reiter
 * in der Mitte. Links wird gehandelt, in der Mitte informiert, rechts geredet.
 */
const ROUTES: Record<string, string> = {
  Overview: 'uebersicht',
  Finance: 'finanzen',
  Personnel: 'personal',
  Ticketing: 'ticketing',
  Contacts: 'kontakte',
  Workspace: 'aufgaben',
  Calendar: 'kalender',
  Documents: 'dokumente',
  Touring: 'touring',
  Production: 'produktionen',
};

/** Das Zeichen, unter dem ein Modul in der Leiste steht. */
const ICONS: Record<string, IconName> = {
  Overview: 'uebersicht',
  Finance: 'finanzen',
  Personnel: 'personal',
  Ticketing: 'ticketing',
  Contacts: 'kontakte',
  Workspace: 'aufgaben',
  Calendar: 'kalender',
  Artwork: 'spielplan',
  Chat: 'chat',
  Documents: 'dokumente',
  Touring: 'touring',
  Production: 'produktion',
};

@Injectable({ providedIn: 'root' })
export class ModulesService {
  private readonly http = inject(HttpClient);
  private readonly loaded = signal<ModuleInfo[]>([]);

  /** Alle Module, auch die abgeschalteten -- für die Einstellungsseite. */
  readonly all = this.loaded.asReadonly();

  /** Nur was dieses Haus eingeschaltet hat -- und was einen Reiter hat. */
  readonly tabs = computed(() =>
    this.loaded()
      .filter((m) => m.isEnabled && ROUTES[m.key])
      .map((m) => ({ ...m, path: ROUTES[m.key], icon: ICONS[m.key] })),
  );

  async load(): Promise<void> {
    try {
      this.loaded.set(await firstValueFrom(this.http.get<ModuleInfo[]>('/api/modules')));
    } catch {
      this.loaded.set([]);
    }
  }

  /**
   * Schaltet ein Modul an oder ab und liest den Stand neu. Die Reiter oben
   * hängen an demselben Signal und folgen daher sofort.
   */
  async set(module: number, isEnabled: boolean, note: string | null = null): Promise<void> {
    await firstValueFrom(this.http.put(`/api/modules/${module}`, { isEnabled, note }));
    await this.load();
  }

  clear(): void {
    this.loaded.set([]);
  }

  isEnabled(key: string): boolean {
    return this.loaded().some((m) => m.key === key && m.isEnabled);
  }
}
