import { Injectable, signal } from '@angular/core';
import { IconName } from '../shared/icon.component';

export interface QuickAction {
  readonly label: string;
  readonly icon: IconName;
  readonly primary?: boolean;
  readonly run?: () => void;
}

export interface SubArea {
  readonly label: string;
  readonly path: string;
  readonly badge?: string;

  /** Ohne Zeichen steht der Eintrag eingerückt da, wo sonst das Zeichen wäre. */
  readonly icon?: IconName;
}

/** Eine Auswahl in der Leiste, etwa „PDF" unter „Dateityp". */
export interface FilterOption {
  readonly key: string;
  readonly label: string;
  readonly count?: number;
  readonly active: boolean;
}

/**
 * Filter, die eine Seite links anbietet -- wo sonst nur Aktionen stehen.
 * Eine Gruppe ist eine Frage („Dateityp"), ihre Optionen schließen sich aus;
 * die Seite entscheidet, was ein Klick bewirkt.
 */
export interface FilterGroup {
  readonly key: string;
  readonly heading: string;
  readonly options: readonly FilterOption[];
  readonly choose: (key: string) => void;
}

/**
 * Was links steht, hängt davon ab, wo man ist. Jede Seite meldet ihre Aktionen
 * hier an -- so bleibt die Leiste eine Leiste und wird nicht zu einem zweiten
 * Navigationsbaum.
 */
@Injectable({ providedIn: 'root' })
export class ActionsRegistry {
  readonly title = signal('Aktionen');
  readonly actions = signal<readonly QuickAction[]>([]);
  readonly areas = signal<readonly SubArea[]>([]);
  readonly filters = signal<readonly FilterGroup[]>([]);

  /** Setzt alles neu -- auch die Filter: Die gehören der Seite, die sie gemeldet hat. */
  set(title: string, actions: readonly QuickAction[], areas: readonly SubArea[] = []): void {
    this.title.set(title);
    this.actions.set(actions);
    this.areas.set(areas);
    this.filters.set([]);
  }

  clear(): void {
    this.set('Aktionen', [], []);
  }
}
