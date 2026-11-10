import { Component, computed, input } from '@angular/core';
import { MetricSegment } from '../core/metrics';

interface Arc {
  readonly label: string;
  readonly value: number;
  readonly color: string;
  readonly dash: string;
  readonly offset: number;
}

/**
 * Welche Farbe ein Platz trägt. Platz 0 ist kein Platz, sondern der
 * zusammengefasste Rest: Grau, damit er nicht wie eine eigene Kategorie
 * aussieht.
 */
export function colorOf(slot: number): string {
  return slot === 0 ? 'var(--color-content-faint)' : `var(--color-chart-${slot})`;
}

/** Der Umfang des Kreises ist genau 100 -- dann ist ein Anteil sein Prozentwert. */
const RADIUS = 15.915494309189533;

/** Die Lücke zwischen zwei Scheiben, in denselben Einheiten wie der Anteil. */
const GAP = 1.4;

/**
 * Ein Donut aus reinem SVG, ohne Bibliothek.
 *
 * Die Scheiben tragen ihre Werte auch als Text in der Legende: Farbe allein
 * trägt die Information nicht, sonst hinge sie am Farbsehen des Betrachters.
 */
@Component({
  selector: 'souffleur-donut',
  templateUrl: './donut.component.html',
})
export class DonutComponent {
  readonly segments = input.required<readonly MetricSegment[]>();
  readonly center = input('');
  readonly centerNote = input('');

  protected readonly radius = RADIUS;

  protected readonly total = computed(() =>
    this.segments().reduce((sum, segment) => sum + segment.value, 0),
  );

  protected readonly arcs = computed<Arc[]>(() => {
    const segments = this.segments().filter((segment) => segment.value > 0);
    const total = this.total();

    if (total <= 0) {
      return [];
    }

    // Eine einzige Scheibe bekommt keine Lücke -- sonst fehlte dem vollen
    // Kreis ein Stück, obwohl nichts anderes danebenliegt.
    const gap = segments.length > 1 ? GAP : 0;
    let start = 0;

    return segments.map((segment) => {
      const share = (segment.value / total) * 100;
      const length = Math.max(share - gap, 0.6);
      const arc: Arc = {
        label: segment.label,
        value: segment.value,
        color: colorOf(segment.slot),
        dash: `${length} ${100 - length}`,
        offset: 25 - start,
      };
      start += share;
      return arc;
    });
  });

  /**
   * Die Legende. Sie traegt die Werte als Text -- die Scheiben allein taeten
   * das nur fuer den, der die Farben unterscheiden kann.
   */
  protected readonly shown = computed(() => {
    const total = this.total();

    return this.segments()
      .filter((segment) => segment.value > 0)
      .map((segment) => ({
        ...segment,
        color: colorOf(segment.slot),
        text: segment.note ?? String(segment.value),
        share: total > 0 ? `${Math.round((segment.value / total) * 100)} %` : '',
      }));
  });
}
