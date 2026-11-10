import { Directive, ElementRef, inject, output } from '@angular/core';
import { AnsageService } from './ansage.service';

/** Ab hier gilt es als Ziehen und nicht mehr als Klick. */
const SCHWELLE = 5;

const GREIFHAND = 'souffleur-greifhand';

/**
 * Macht die Kinder eines Behälters umsortierbar -- mit dem Zeiger und mit
 * der Tastatur.
 *
 * Jedes Kind, das mitmachen soll, trägt `data-sortier-id`. Die Direktive
 * ordnet selbst nichts um: Sie meldet die neue Reihenfolge, und wer sie
 * einsetzt, entscheidet, was damit geschieht. So bleibt die Liste da, wo sie
 * hingehört -- im Signal der Komponente, nicht im DOM.
 *
 * Gezogen wird mit Pointer-Ereignissen, nicht mit HTML5-Drag-and-Drop: Die
 * funktionieren auch auf einem Tablet, und das Aussehen bleibt unseres.
 *
 * Mit der Tastatur: Strg (oder Cmd) und Pfeiltaste verschieben das gerade
 * fokussierte Kind. Ohne das wäre die Reihenfolge nur mit der Maus zu
 * ändern, und wer die nicht benutzt, bliebe bei der gelieferten.
 *
 * Dieselbe Direktive trägt die Reiterleiste oben und die Knöpfe links; die
 * Richtung liest sie aus der Geometrie ab.
 *
 * Trägt ein Kind einen Griff (`data-sortier-griff`), greift nur der: Eine
 * Tabellenzeile voller Knöpfe und Felder darf sich sonst nicht anfassen
 * lassen, ohne dass man etwas davon meint.
 *
 * Für die Ansage an den Screenreader zählt `data-sortier-name`, falls
 * vorhanden -- sonst der Text des Kindes.
 */
@Directive({
  selector: '[souffleurSortierbar]',
  host: {
    '(pointerdown)': 'greifen($event)',
    '(keydown)': 'taste($event)',
  },
})
export class SortierbarDirective {
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly ansage = inject(AnsageService);

  /** Die Schlüssel in ihrer neuen Reihenfolge. */
  readonly neueReihenfolge = output<readonly string[]>();

  private gegriffen: HTMLElement | null = null;
  private start = 0;
  private zieht = false;
  private quer = false;

  /** Vor welchem Kind eingefügt würde; die Länge der Liste heißt „ans Ende". */
  private ziel = -1;

  /**
   * Wo die Kinder beim Greifen lagen.
   *
   * Eingefroren und nicht bei jedem Ruck neu gemessen: Das gezogene Kind
   * wandert mit dem Zeiger, seine Mitte wandert mit, und die Rechnung,
   * zwischen welche beiden es gehört, liefe der eigenen Bewegung nach.
   */
  private rahmen: DOMRect[] = [];

  private get teile(): HTMLElement[] {
    return Array.from(this.host.nativeElement.querySelectorAll<HTMLElement>('[data-sortier-id]'));
  }

  /**
   * Die Richtung steht nicht im Aufruf, sondern in der Geometrie: Liegen die
   * ersten beiden Kinder nebeneinander, wird waagerecht sortiert.
   */
  private waagerecht(teile: readonly HTMLElement[]): boolean {
    if (teile.length < 2) {
      return false;
    }

    const erstes = teile[0].getBoundingClientRect();
    const zweites = teile[1].getBoundingClientRect();
    return Math.abs(zweites.left - erstes.left) > Math.abs(zweites.top - erstes.top);
  }

  protected greifen(event: PointerEvent): void {
    if (event.button !== 0) {
      return;
    }

    const teil = (event.target as HTMLElement).closest<HTMLElement>('[data-sortier-id]');
    if (!teil || !this.host.nativeElement.contains(teil)) {
      return;
    }

    const griff = teil.querySelector<HTMLElement>('[data-sortier-griff]');
    if (griff && !griff.contains(event.target as Node)) {
      return;
    }

    const teile = this.teile;
    this.gegriffen = teil;
    this.zieht = false;
    this.quer = this.waagerecht(teile);
    this.rahmen = teile.map((t) => t.getBoundingClientRect());
    this.start = this.quer ? event.clientX : event.clientY;

    // Am Fenster, nicht am Element: Wer schnell zieht, ist mit dem Zeiger
    // längst daneben, und das Loslassen soll trotzdem ankommen.
    const schluss = new AbortController();
    window.addEventListener('pointermove', (e) => this.ziehen(e as PointerEvent), {
      signal: schluss.signal,
    });
    window.addEventListener(
      'pointerup',
      () => {
        this.loslassen();
        schluss.abort();
      },
      { signal: schluss.signal },
    );
  }

  private ziehen(event: PointerEvent): void {
    if (!this.gegriffen) {
      return;
    }

    const jetzt = this.quer ? event.clientX : event.clientY;
    const weg = jetzt - this.start;

    if (!this.zieht) {
      if (Math.abs(weg) < SCHWELLE) {
        return;
      }

      this.zieht = true;
      this.anheben();
    }

    // Das gezogene Kind folgt dem Zeiger -- das ist das deutlichste Zeichen
    // dafür, dass man es gerade trägt.
    this.gegriffen.style.transform = this.quer ? `translateX(${weg}px)` : `translateY(${weg}px)`;

    // Vor dem ersten Kind, dessen Mitte hinter dem Zeiger liegt.
    this.ziel = this.rahmen.findIndex((masse) => {
      const mitte = this.quer ? masse.left + masse.width / 2 : masse.top + masse.height / 2;
      return jetzt < mitte;
    });

    if (this.ziel < 0) {
      this.ziel = this.rahmen.length;
    }

    this.ausweichen();
  }

  /**
   * Die anderen machen Platz.
   *
   * Ein Strich zwischen zwei Kindern waere das Naheliegende, taugt hier aber
   * nicht: Die Reiterleiste traegt die Markenfarbe, und ein Strich in
   * derselben Farbe ist dort unsichtbar. Vor allem aber zeigt eine Luecke
   * unmittelbarer, was gleich passiert -- man sieht den Platz, in den das
   * Element faellt, und muss keine Markierung deuten.
   */
  private ausweichen(): void {
    const teile = this.teile;
    const von = this.gegriffen ? teile.indexOf(this.gegriffen) : -1;
    if (von < 0 || this.rahmen.length < 2) {
      return;
    }

    // Die Luecke zwischen zwei Kindern gehoert zum Platz, den das gezogene
    // hinterlaesst -- sonst ruecken die anderen zu wenig.
    const luecke = this.quer
      ? this.rahmen[1].left - this.rahmen[0].right
      : this.rahmen[1].top - this.rahmen[0].bottom;

    const schritt =
      (this.quer ? this.rahmen[von].width : this.rahmen[von].height) + Math.max(luecke, 0);

    // Derselbe Index wie beim Loslassen: Wer nach hinten zieht, hinterlaesst
    // eine Luecke, und der gemerkte Zielindex zaehlt das noch nicht
    // herausgenommene Kind mit.
    const nach = this.ziel > von ? this.ziel - 1 : this.ziel;
    const achse = this.quer ? 'X' : 'Y';

    teile.forEach((teil, index) => {
      if (index === von) {
        return;
      }

      const versatz =
        nach > von && index > von && index <= nach
          ? -schritt
          : nach < von && index >= nach && index < von
            ? schritt
            : 0;

      // Das Ausweichen darf gleiten, das gezogene Kind nicht -- es soll dem
      // Zeiger ohne Verzug folgen.
      teil.style.transition = 'transform 150ms ease';
      teil.style.transform = versatz === 0 ? '' : `translate${achse}(${versatz}px)`;
    });
  }

  /** Das gezogene Kind über die anderen heben. */
  private anheben(): void {
    if (!this.gegriffen) {
      return;
    }

    this.gegriffen.style.position = 'relative';
    this.gegriffen.style.zIndex = '60';
    this.gegriffen.style.opacity = '0.85';
    this.gegriffen.style.boxShadow = '0 6px 16px rgb(0 0 0 / 0.3)';
    this.gegriffen.style.borderRadius = '0.375rem';

    // Eine Regel für alles und mit !important, nicht als Stil am Körper: Ein
    // Link bringt seinen eigenen Zeiger mit und überschriebe den geerbten.
    const regel = document.createElement('style');
    regel.id = GREIFHAND;
    regel.textContent = '*, *::before, *::after { cursor: grabbing !important; }';
    document.head.appendChild(regel);

    // Sonst markiert der Zug nebenbei den Text der Beschriftungen.
    document.body.style.userSelect = 'none';
  }

  private loslassen(): void {
    const gegriffen = this.gegriffen;
    const gezogen = this.zieht;
    const ziel = this.ziel;
    const teile = this.teile;

    this.aufraeumen();

    if (!gegriffen || !gezogen || ziel < 0) {
      return;
    }

    // Nach dem Ziehen feuert noch ein Klick auf dem Kind. Ungebremst würde
    // ein verschobener Reiter die Seite wechseln.
    window.addEventListener(
      'click',
      (e) => {
        e.stopPropagation();
        e.preventDefault();
      },
      { capture: true, once: true },
    );

    const von = teile.indexOf(gegriffen);
    if (von < 0) {
      return;
    }

    // Wer nach hinten zieht, hinterlässt eine Lücke: Der gemerkte Zielindex
    // zählt das noch nicht herausgenommene Kind mit.
    const nach = ziel > von ? ziel - 1 : ziel;
    if (nach === von) {
      return;
    }

    this.melden(
      teile.map((teil) => teil.dataset['sortierId'] ?? ''),
      von,
      nach,
    );
  }

  private aufraeumen(): void {
    for (const eigenschaft of [
      'transform',
      'position',
      'z-index',
      'opacity',
      'box-shadow',
      'border-radius',
    ]) {
      this.gegriffen?.style.removeProperty(eigenschaft);
    }

    for (const teil of this.teile) {
      teil.style.removeProperty('transform');
      teil.style.removeProperty('transition');
    }

    document.getElementById(GREIFHAND)?.remove();
    document.body.style.removeProperty('user-select');

    this.gegriffen = null;
    this.zieht = false;
    this.ziel = -1;
    this.rahmen = [];
  }

  protected taste(event: KeyboardEvent): void {
    if (!event.ctrlKey && !event.metaKey) {
      return;
    }

    const richtung =
      event.key === 'ArrowLeft' || event.key === 'ArrowUp'
        ? -1
        : event.key === 'ArrowRight' || event.key === 'ArrowDown'
          ? 1
          : 0;

    if (richtung === 0) {
      return;
    }

    const teil = (event.target as HTMLElement).closest<HTMLElement>('[data-sortier-id]');
    if (!teil) {
      return;
    }

    const teile = this.teile;
    const von = teile.indexOf(teil);
    const nach = von + richtung;
    if (von < 0 || nach < 0 || nach >= teile.length) {
      return;
    }

    // Sonst scrollt die Pfeiltaste nebenbei die Leiste.
    event.preventDefault();

    this.melden(
      teile.map((t) => t.dataset['sortierId'] ?? ''),
      von,
      nach,
    );

    // Der Fokus bleibt von allein am Element: @for mit track verschiebt den
    // vorhandenen Knoten, statt einen neuen zu bauen.
    //
    // Ein Block der Übersicht trägt seinen Namen in `data-sortier-name`: Sein
    // ganzer Text wären drei Kacheln voller Zahlen.
    const name = teil.dataset['sortierName'] ?? teil.textContent?.trim();
    this.ansage.sage(`${name}, Platz ${nach + 1} von ${teile.length}`);
  }

  private melden(ids: readonly string[], von: number, nach: number): void {
    const neu = [...ids];
    const [bewegt] = neu.splice(von, 1);
    neu.splice(nach, 0, bewegt);
    this.neueReihenfolge.emit(neu);
  }
}
