import {
  Component,
  ElementRef,
  OnDestroy,
  afterNextRender,
  input,
  output,
  viewChild,
} from '@angular/core';

/** Wie viele Fenster gerade offen stehen -- das Blatt darunter scrollt erst wieder, wenn alle zu sind. */
let open = 0;

/**
 * Das Fenster über allem.
 *
 * Anlegen und Ändern passieren hier, nicht unten im Blatt: Ein Formular, das
 * an die Liste angehängt wird, findet nur, wer danach sucht und scrollt. Wer
 * fertig ist, schließt das Fenster und steht wieder in seiner gewohnten
 * Umgebung.
 *
 * Ein Klick auf den Hintergrund schließt bewusst nicht -- daneben zu treffen
 * wäre sonst dasselbe wie Abbrechen, und ein halb ausgefüllter Beleg wäre weg.
 * Es gibt zwei Wege hinaus: Escape und „Schließen".
 */
@Component({
  selector: 'souffleur-dialog',
  templateUrl: './dialog.component.html',
})
export class DialogComponent implements OnDestroy {
  readonly heading = input.required<string>();

  /** Eine Zeile unter der Überschrift, wenn das Fenster eine Einordnung braucht. */
  readonly note = input('');

  readonly size = input<'schmal' | 'breit' | 'weit'>('schmal');

  readonly closed = output<void>();

  private readonly panel = viewChild.required<ElementRef<HTMLElement>>('panel');

  /** Wohin die Tastatur zurückkehrt, wenn das Fenster wieder zu ist. */
  private readonly opener = document.activeElement as HTMLElement | null;

  constructor() {
    open += 1;
    document.body.classList.add('overflow-hidden');

    // Erst nach dem Zeichnen: vorher gibt es nichts, worauf der Fokus
    // springen könnte.
    afterNextRender(() => this.focusFirst());
  }

  ngOnDestroy(): void {
    open = Math.max(0, open - 1);
    if (open === 0) {
      document.body.classList.remove('overflow-hidden');
    }

    this.opener?.focus?.();
  }

  /**
   * Escape schließt, Tab bleibt drin. Ohne das Zweite wandert der Fokus
   * hinter das Fenster, und wer nur mit der Tastatur arbeitet, füllt
   * unsichtbare Felder aus.
   */
  protected onKeydown(event: KeyboardEvent): void {
    if (event.key === 'Escape') {
      event.stopPropagation();
      this.closed.emit();
      return;
    }

    if (event.key !== 'Tab') {
      return;
    }

    const items = this.focusable();
    if (items.length === 0) {
      return;
    }

    const first = items[0];
    const last = items[items.length - 1];

    if (event.shiftKey && document.activeElement === first) {
      event.preventDefault();
      last.focus();
    } else if (!event.shiftKey && document.activeElement === last) {
      event.preventDefault();
      first.focus();
    }
  }

  private focusFirst(): void {
    const items = this.focusable();
    (items[0] ?? this.panel().nativeElement).focus();
  }

  private focusable(): HTMLElement[] {
    const selector =
      'a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), ' +
      'textarea:not([disabled]), [tabindex]:not([tabindex="-1"])';

    return Array.from(this.panel().nativeElement.querySelectorAll<HTMLElement>(selector)).filter(
      (item) => item.offsetParent !== null || item === document.activeElement,
    );
  }
}
