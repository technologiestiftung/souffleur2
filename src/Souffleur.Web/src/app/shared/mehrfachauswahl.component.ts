import { Component, computed, input, model } from '@angular/core';
import { FormsModule } from '@angular/forms';

export interface Wahl {
  id: string;
  name: string;
}

/**
 * Mehrere aus einer Liste wählen: die Gewählten als Chips, darunter ein
 * Auswahlfeld, das nur noch anbietet, was fehlt. Für Beteiligte an einer
 * Aufgabe, verknüpfte Kontakte -- überall, wo „eine oder mehrere" gefragt ist.
 *
 * Ein einfaches Auswahlfeld und keine Suchliste: Die Listen im Haus haben
 * zwanzig, nicht zweitausend Einträge, und das native Feld lässt sich mit der
 * Tastatur bedienen, ohne dass hier etwas nachgebaut werden müsste.
 */
@Component({
  selector: 'souffleur-mehrfachauswahl',
  imports: [FormsModule],
  template: `
    <span class="mb-1 block text-xs font-medium text-content-muted">{{ label() }}</span>
    @if (chips().length > 0) {
      <ul class="m-0 mb-1 flex list-none flex-wrap gap-1 p-0" [attr.aria-label]="label()">
        @for (chip of chips(); track chip.id) {
          <li
            class="flex items-center gap-1 rounded-full border border-border bg-surface-sunken py-0.5 pr-1 pl-2 text-xs text-content"
          >
            {{ chip.name }}
            <button
              type="button"
              (click)="entfernen(chip.id)"
              [attr.aria-label]="chip.name + ' entfernen'"
              class="grid size-4 cursor-pointer place-items-center rounded-full border-0 bg-transparent text-content-faint hover:text-critical"
            >
              ✕
            </button>
          </li>
        }
      </ul>
    }
    <select
      [ngModel]="''"
      (ngModelChange)="hinzufuegen($event)"
      [attr.aria-label]="label() + ' hinzufügen'"
      class="w-44 rounded-md border border-border bg-surface px-2 py-1.5 text-sm text-content"
    >
      <option value="">{{ offen().length === 0 ? '— niemand mehr —' : '+ hinzufügen' }}</option>
      @for (wahl of offen(); track wahl.id) {
        <option [value]="wahl.id">{{ wahl.name }}</option>
      }
    </select>
  `,
  host: { class: 'block' },
})
export class MehrfachauswahlComponent {
  readonly label = input.required<string>();
  readonly optionen = input.required<readonly Wahl[]>();
  readonly gewaehlt = model<string[]>([]);

  protected readonly chips = computed(() => {
    const byId = new Map(this.optionen().map((o) => [o.id, o]));
    // Wer gewählt, aber nicht mehr in der Liste ist (stillgelegt), bleibt sichtbar.
    return this.gewaehlt().map((id) => byId.get(id) ?? { id, name: 'Nicht mehr in der Liste' });
  });

  protected readonly offen = computed(() => {
    const drin = new Set(this.gewaehlt());
    return this.optionen().filter((o) => !drin.has(o.id));
  });

  protected hinzufuegen(id: string): void {
    if (!id || this.gewaehlt().includes(id)) {
      return;
    }
    this.gewaehlt.update((liste) => [...liste, id]);
  }

  protected entfernen(id: string): void {
    this.gewaehlt.update((liste) => liste.filter((x) => x !== id));
  }
}
