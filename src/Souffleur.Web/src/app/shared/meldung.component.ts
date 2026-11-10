import { Component, computed, input } from '@angular/core';

/**
 * Eine Meldung, die auch ankommt.
 *
 * Zu sehen ist dieselbe Box wie vorher. Neu ist die Rolle: `alert` lässt den
 * Screenreader vorlesen, was hier erscheint -- vorher stand die Meldung nur
 * da, und wer nicht hinsah, erfuhr nichts.
 *
 * Bewusst die Rolle am Element selbst und keine eigene Live-Region daneben:
 * Eine zweite Stelle, an der derselbe Satz steht, ist für jeden, der den Text
 * sucht -- Screenreader im Durchlesemodus wie Browsertest -- ein Dublett.
 * `alert` ist für genau diesen Fall gedacht: eine Meldung, die dynamisch
 * erscheint.
 *
 * Abstand und Breite setzt die Aufrufstelle am Element selbst; die Komponente
 * bringt nur ihre eigene Gestalt mit. Ob überhaupt gemeldet wird, entscheidet
 * weiterhin das @if davor -- sonst risse eine leere Box ein Loch ins Blatt.
 */
@Component({
  selector: 'souffleur-meldung',
  templateUrl: './meldung.component.html',
  host: {
    class: 'block',
    '[attr.role]': "art() === 'fehler' ? 'alert' : 'status'",
  },
})
export class MeldungComponent {
  /** Ein Satz oder mehrere -- die meisten Prüfungen liefern eine Liste. */
  readonly texte = input.required<string | readonly string[]>();

  /** „fehler" unterbricht beim Vorlesen, „hinweis" wartet auf eine Pause. */
  readonly art = input<'fehler' | 'hinweis'>('fehler');

  protected readonly zeilen = computed(() => {
    const roh = this.texte();
    return (typeof roh === 'string' ? [roh] : roh).filter((zeile) => zeile.length > 0);
  });
}
