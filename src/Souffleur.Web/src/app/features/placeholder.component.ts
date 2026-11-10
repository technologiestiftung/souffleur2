import { Component, inject, input } from '@angular/core';
import { ActionsRegistry } from '../shell/actions.registry';

/**
 * Für Bereiche, die es noch nicht gibt. Sagt ehrlich, was fehlt, statt eine
 * leere Tabelle zu zeigen.
 */
@Component({
  selector: 'souffleur-placeholder',
  template: `
    <div class="p-6">
      <h1 class="font-display text-2xl font-bold tracking-tight">{{ title() }}</h1>
      <div class="mt-4 max-w-xl rounded-xl border border-border bg-surface-raised p-6">
        <p class="text-sm text-content-muted">{{ note() }}</p>
      </div>
    </div>
  `,
})
export class PlaceholderComponent {
  readonly title = input('Bereich');
  readonly note = input('Dieser Bereich entsteht noch.');

  private readonly actions = inject(ActionsRegistry);

  constructor() {
    this.actions.clear();
  }
}
