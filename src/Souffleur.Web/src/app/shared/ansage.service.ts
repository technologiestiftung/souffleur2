import { Injectable } from '@angular/core';

/**
 * Sagt etwas an, das nirgends auf dem Bildschirm steht.
 *
 * Abgrenzung zur Meldungskomponente: Die zeigt ihren Text und trägt dafür
 * role="alert" -- dort wäre eine zweite Stelle mit demselben Satz ein
 * Dublett. Hier gibt es nichts zu sehen. Dass ein Reiter von Platz drei auf
 * Platz zwei gewandert ist, sieht man; mit der Tastatur hört man es sonst
 * nicht.
 *
 * Die Region hängt am Body und entsteht einmal. Eine, die erst mit der
 * Meldung in den Baum käme, wird oft nicht beobachtet und bliebe stumm.
 */
@Injectable({ providedIn: 'root' })
export class AnsageService {
  private region: HTMLElement | null = null;

  sage(text: string): void {
    const region = (this.region ??= this.anlegen());

    // Zweimal derselbe Satz ändert nichts am Inhalt, und was sich nicht
    // ändert, wird nicht vorgelesen. Wer zweimal dieselbe Stelle ansteuert,
    // hörte sonst nur beim ersten Mal etwas.
    region.textContent = '';
    setTimeout(() => (region.textContent = text), 50);
  }

  private anlegen(): HTMLElement {
    const region = document.createElement('div');
    region.className = 'sr-only';
    region.setAttribute('role', 'status');
    region.setAttribute('aria-live', 'polite');
    document.body.appendChild(region);
    return region;
  }
}
