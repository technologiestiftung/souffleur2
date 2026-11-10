import { Component, OnInit, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MetricTile } from '../core/metrics';
import { MetricsService } from '../core/metrics.service';
import { BarsComponent } from './bars.component';
import { DonutComponent } from './donut.component';
import { IconComponent } from './icon.component';

/**
 * Die Reihe Kacheln über jedem Modul -- und dieselbe Reihe noch einmal in der
 * Übersicht. Beide fragen denselben Dienst; es gibt die Kennzahlen nur
 * einmal, nicht einmal je Seite.
 */
@Component({
  selector: 'souffleur-metric-strip',
  imports: [DonutComponent, BarsComponent, RouterLink, IconComponent],
  templateUrl: './metric-strip.component.html',
})
export class MetricStripComponent implements OnInit {
  private readonly metrics = inject(MetricsService);

  /** Der Pfad des Moduls, etwa „finanzen". */
  readonly module = input.required<string>();

  /** Gesetzt, wenn die Reihe in der Übersicht steht: Überschrift mit Verweis. */
  readonly heading = input('');

  protected readonly tiles = signal<MetricTile[]>([]);

  /**
   * Ob die Reihe aufgeklappt ist. Je Modul gemerkt: Auf dem Kalender zählt
   * jede Zeile Höhe, im Journal stört die Reihe niemanden.
   */
  protected readonly shown = signal(true);
  protected readonly loading = signal(true);
  protected readonly placeholders = [0, 1, 2, 3];

  async ngOnInit(): Promise<void> {
    // Ein Modul ohne Kennzahlen bekommt nicht einmal den Platzhalter -- sonst
    // blitzte in der Übersicht eine Reihe auf, die gleich wieder verschwindet.
    if (!this.metrics.supports(this.module())) {
      this.loading.set(false);
      return;
    }

    this.shown.set(remembered(this.module()));
    this.tiles.set(await this.metrics.tilesFor(this.module()));
    this.loading.set(false);
  }

  protected toggle(): void {
    const next = !this.shown();
    this.shown.set(next);
    remember(this.module(), next);
  }

  protected hasData(tile: MetricTile): boolean {
    return tile.segments.some((segment) => segment.value > 0);
  }
}

/** Zugeklappt oder nicht -- im Browser gemerkt, nicht auf dem Server. */
function remembered(module: string): boolean {
  try {
    return localStorage.getItem(`souffleur:kennzahlen:${module}`) !== 'zu';
  } catch {
    // Privates Fenster oder gesperrte Seitendaten: dann eben aufgeklappt.
    return true;
  }
}

function remember(module: string, open: boolean): void {
  try {
    localStorage.setItem(`souffleur:kennzahlen:${module}`, open ? 'auf' : 'zu');
  } catch {
    // Nicht merken zu können ist kein Grund, nichts anzuzeigen.
  }
}
