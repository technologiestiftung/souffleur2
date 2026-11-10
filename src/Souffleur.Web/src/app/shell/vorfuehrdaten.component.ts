import { Component, ElementRef, effect, inject, signal } from '@angular/core';
import { DemoHouse, DemoHousesService } from '../core/demo-houses.service';
import { HouseService } from '../core/house.service';

/**
 * Die Wortmarke „souffleur" in der Kopfzeile -- und an ihr die
 * Kennzeichnung „Vorführdaten".
 *
 * Erfundenes als solches kennzeichnen: Einem Termin sieht man das nicht an,
 * einem abonnierten im Telefon erst recht nicht. Früher ein eigenes Schild
 * neben der Wortmarke; das nahm den Reitern die Breite. Jetzt ein kleines
 * Zeichen mit Hinweis an der Wortmarke selbst. In der Entwicklung ist die
 * Wortmarke zugleich der Schalter, mit dem sich das Haus wechseln lässt --
 * wer vorführt, soll dem einen Haus seinen Spielplan zeigen und dem
 * anderen den seinen, ohne sich neu anzumelden.
 */
@Component({
  selector: 'souffleur-vorfuehrdaten',
  templateUrl: './vorfuehrdaten.component.html',
  host: {
    class: 'relative',
    '(document:click)': 'aussen($event)',
    '(document:keydown.escape)': 'offen.set(false)',
  },
})
export class VorfuehrdatenComponent {
  protected readonly house = inject(HouseService);
  protected readonly demo = inject(DemoHousesService);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  protected readonly offen = signal(false);

  /** Der Schlüssel des Hauses, das gerade geladen wird -- solange ist das Menü gesperrt. */
  protected readonly wechselt = signal<string | null>(null);

  private geladen = false;

  constructor() {
    // Erst fragen, wenn feststeht, dass hier Erfundenes steht. Im Betrieb
    // bleibt der Aufruf so ganz aus.
    effect(() => {
      if (this.house.hasDemoData() && !this.geladen) {
        this.geladen = true;
        void this.demo.load();
      }
    });
  }

  protected umschalten(): void {
    this.offen.update((o) => !o);
  }

  /** Ein Klick daneben schließt -- wie bei jedem Aufklappmenü. */
  protected aussen(event: MouseEvent): void {
    if (this.offen() && !this.host.nativeElement.contains(event.target as Node)) {
      this.offen.set(false);
    }
  }

  protected async wechseln(ziel: DemoHouse): Promise<void> {
    if (ziel.isCurrent || this.wechselt() !== null) {
      this.offen.set(false);
      return;
    }

    this.wechselt.set(ziel.key);
    try {
      await this.demo.switchTo(ziel.key);
      // Jede Ansicht hält ihren Ausschnitt des alten Bestands. Neu laden ist
      // ehrlicher als jeden Dienst einzeln zum Vergessen zu überreden.
      window.location.reload();
    } catch {
      this.wechselt.set(null);
    }
  }
}
