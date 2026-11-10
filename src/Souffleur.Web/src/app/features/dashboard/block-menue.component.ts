import { Component, ElementRef, inject, input, output, signal } from '@angular/core';
import { IconComponent } from '../../shared/icon.component';

/**
 * Die drei Punkte am Ende eines Blocks der Übersicht.
 *
 * Dahinter steckt, was man mit einem Block sonst noch tun kann: ihn
 * ausblenden oder ohne Maus verschieben. Das Ziehen am Griff bleibt der
 * schnelle Weg; das Menü ist der ausgeschriebene.
 */
@Component({
  selector: 'souffleur-block-menue',
  imports: [IconComponent],
  templateUrl: './block-menue.component.html',
  host: {
    class: 'relative',
    '(document:click)': 'aussen($event)',
    '(document:keydown.escape)': 'offen.set(false)',
  },
})
export class BlockMenueComponent {
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  /** Wie der Block heißt -- für den Namen des Knopfs. */
  readonly label = input.required<string>();

  /** Am Anfang oder am Ende: Dann gibt es dorthin nichts zu verschieben. */
  readonly erster = input(false);
  readonly letzter = input(false);

  readonly ausblenden = output<void>();
  readonly nachOben = output<void>();
  readonly nachUnten = output<void>();

  protected readonly offen = signal(false);

  protected umschalten(): void {
    this.offen.update((o) => !o);
  }

  /** Ein Klick daneben schließt -- wie bei jedem Aufklappmenü. */
  protected aussen(event: MouseEvent): void {
    if (this.offen() && !this.host.nativeElement.contains(event.target as Node)) {
      this.offen.set(false);
    }
  }

  protected waehle(was: 'oben' | 'unten' | 'aus'): void {
    this.offen.set(false);
    if (was === 'oben') {
      this.nachOben.emit();
    } else if (was === 'unten') {
      this.nachUnten.emit();
    } else {
      this.ausblenden.emit();
    }
  }
}
