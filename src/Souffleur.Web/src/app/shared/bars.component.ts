import { Component, computed, input } from '@angular/core';
import { MetricSegment } from '../core/metrics';
import { colorOf } from './donut.component';

/**
 * Liegende Balken für das, was ein Donut nicht trägt: Größen, die sich nicht
 * zu einem Ganzen addieren, und Namen, die Platz brauchen.
 */
@Component({
  selector: 'souffleur-bars',
  templateUrl: './bars.component.html',
})
export class BarsComponent {
  readonly segments = input.required<readonly MetricSegment[]>();

  protected readonly rows = computed(() => {
    const segments = this.segments().filter((segment) => segment.value > 0);
    const largest = Math.max(...segments.map((segment) => segment.value), 0);

    // Alle Balken in derselben Farbe: Sie zeigen eine Größe, keine
    // Zugehörigkeit. Verschiedene Farben würden die Länge ein zweites Mal
    // erzählen und den Eindruck erwecken, die Farbe bedeute etwas. Nur der
    // zusammengefasste Rest fällt heraus -- er steht neben der Reihe.
    return segments.map((segment) => ({
      label: segment.label,
      text: segment.note ?? String(segment.value),
      // Am größten Balken ausgerichtet, nicht an der Summe: Sonst bliebe bei
      // vielen Zeilen überall nur ein Strich übrig.
      percent: largest > 0 ? (segment.value / largest) * 100 : 0,
      color: colorOf(segment.slot === 0 ? 0 : 1),
    }));
  });
}
