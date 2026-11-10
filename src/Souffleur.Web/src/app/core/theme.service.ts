import { DOCUMENT } from '@angular/common';
import { Injectable, computed, effect, inject, signal } from '@angular/core';

export type ThemeChoice = 'system' | 'light' | 'dark';

const STORAGE_KEY = 'souffleur.theme';

@Injectable({ providedIn: 'root' })
export class ThemeService {
  private readonly document = inject(DOCUMENT);

  /** Was die Person gewählt hat. 'system' folgt dem Betriebssystem. */
  readonly choice = signal<ThemeChoice>(this.restore());

  /** Was das Betriebssystem gerade sagt. */
  private readonly systemDark = signal(this.prefersDark());

  /** Was tatsächlich angezeigt wird. */
  readonly resolved = computed<'light' | 'dark'>(() => {
    const choice = this.choice();
    if (choice !== 'system') {
      return choice;
    }
    return this.systemDark() ? 'dark' : 'light';
  });

  constructor() {
    // Auf Änderungen am Betriebssystem hören, solange 'system' gewählt ist.
    const query = this.document.defaultView?.matchMedia('(prefers-color-scheme: dark)');
    query?.addEventListener('change', (event) => this.systemDark.set(event.matches));

    effect(() => {
      this.document.documentElement.dataset['theme'] = this.resolved();
      this.persist(this.choice());
    });
  }

  set(choice: ThemeChoice): void {
    this.choice.set(choice);
  }

  /** Reihum: System, hell, dunkel. */
  cycle(): void {
    const order: ThemeChoice[] = ['system', 'light', 'dark'];
    const next = order[(order.indexOf(this.choice()) + 1) % order.length];
    this.choice.set(next);
  }

  private prefersDark(): boolean {
    return this.document.defaultView?.matchMedia('(prefers-color-scheme: dark)').matches ?? false;
  }

  private restore(): ThemeChoice {
    try {
      const stored = localStorage.getItem(STORAGE_KEY);
      return stored === 'light' || stored === 'dark' || stored === 'system' ? stored : 'system';
    } catch {
      // Privates Fenster oder gesperrte Speicherung: dann eben jedes Mal neu.
      return 'system';
    }
  }

  private persist(choice: ThemeChoice): void {
    try {
      localStorage.setItem(STORAGE_KEY, choice);
    } catch {
      /* nicht weiter schlimm */
    }
  }
}
