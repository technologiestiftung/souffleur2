import { Component, ElementRef, computed, inject } from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';
import { LayoutService } from '../core/layout.service';
import { ActionsRegistry } from './actions.registry';
import { PreferencesService } from '../core/preferences.service';
import { SortierbarDirective } from '../shared/sortierbar.directive';
import { IconComponent } from '../shared/icon.component';

/**
 * Links wird gehandelt: die Aktionen zum Bereich, in dem man gerade ist, und
 * darunter die Unterbereiche. Lässt sich zusammenklappen und anpinnen.
 *
 * Angepinnt steht die Leiste neben dem Inhalt. Gelöst bleibt der schmale
 * Streifen, und die volle Leiste legt sich auf Wunsch darüber -- und geht
 * nach jeder Aktion, bei Klick daneben oder mit Escape wieder zu.
 */
@Component({
  selector: 'souffleur-actions-sidebar',
  imports: [RouterLink, RouterLinkActive, SortierbarDirective, IconComponent],
  templateUrl: './actions-sidebar.component.html',
  host: {
    class: 'relative flex shrink-0',
    '(document:click)': 'aussen($event)',
    '(document:keydown.escape)': 'layout.closeActionsOverlay()',
    '(mouseenter)': 'schweben(true)',
    '(mouseleave)': 'schweben(false)',
  },
})
export class ActionsSidebarComponent {
  protected readonly layout = inject(LayoutService);
  protected readonly registry = inject(ActionsRegistry);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  protected readonly open = computed(() => this.layout.actionsOpen());

  /** Fest angedockt braucht es den Streifen nicht; sonst steht er immer. */
  protected readonly streifen = computed(() => !this.layout.actionsDocked());

  /**
   * Zugeklappt öffnet sich die Leiste schon, wenn die Maus auf den Streifen
   * kommt, und schließt, wenn sie wieder geht -- mit kleiner Verzögerung,
   * damit ein Vorbeifahren nichts auslöst. Solange etwas in der Leiste
   * den Fokus hat, bleibt sie trotz Maus draußen offen: Wer gerade tippt,
   * will nicht, dass ihm das Feld unter den Fingern verschwindet.
   */
  protected schweben(drin: boolean): void {
    clearTimeout(this.schwebt);
    if (this.layout.actionsDocked()) {
      return;
    }
    this.schwebt = setTimeout(
      () => {
        if (drin) {
          this.layout.openActionsOverlay();
        } else if (!this.host.nativeElement.contains(document.activeElement)) {
          this.layout.closeActionsOverlay();
        }
      },
      drin ? 120 : 300,
    );
  }

  private schwebt?: ReturnType<typeof setTimeout>;

  protected ausfuehren(run: (() => void) | undefined): void {
    run?.();
    this.layout.closeActionsOverlay();
  }

  protected aussen(event: MouseEvent): void {
    if (this.open() && !this.host.nativeElement.contains(event.target as Node)) {
      this.layout.closeActionsOverlay();
    }
  }

  private readonly preferences = inject(PreferencesService);

  /**
   * Je Seite ein eigener Schlüssel: Die Leiste wird von jeder Seite neu
   * gefüllt -- auf den Belegen stehen andere Knöpfe als im Kalender. Eine
   * gemeinsame Reihenfolge wäre für die meisten Seiten die falsche.
   *
   * Als Schlüssel dient die Überschrift der Leiste. Sie ist das Einzige, was
   * die Registry über ihre Herkunft weiß; benennt eine Seite sich um, fängt
   * sie wieder mit der gelieferten Reihenfolge an.
   */
  private readonly schluessel = computed(() => this.registry.title());

  protected readonly aktionen = computed(() =>
    this.preferences.sortiere(
      `aktionen:${this.schluessel()}`,
      this.registry.actions(),
      (aktion) => aktion.label,
    ),
  );

  protected readonly bereiche = computed(() =>
    this.preferences.sortiere(
      `bereiche:${this.schluessel()}`,
      this.registry.areas(),
      (bereich) => bereich.path,
    ),
  );

  protected aktionenSortiert(ids: readonly string[]): void {
    void this.preferences.merke(`aktionen:${this.schluessel()}`, ids);
  }

  protected bereicheSortiert(ids: readonly string[]): void {
    void this.preferences.merke(`bereiche:${this.schluessel()}`, ids);
  }
}
