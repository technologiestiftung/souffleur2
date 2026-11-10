import { DatePipe } from '@angular/common';
import { Component, ElementRef, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { NotificationsService, SouffleurNotification } from '../core/notifications.service';
import { IconComponent } from '../shared/icon.component';

/**
 * Die Glocke in der Kopfzeile.
 *
 * Ein Klick öffnet die Liste, ein Klick auf einen Eintrag führt hin und
 * hakt ihn ab. Gelesen wird einzeln -- „alle gelesen" gibt es trotzdem, für
 * den Montagmorgen nach dem Premierenwochenende.
 */
@Component({
  selector: 'souffleur-glocke',
  imports: [DatePipe, IconComponent, RouterLink],
  templateUrl: './glocke.component.html',
  host: {
    class: 'relative',
    '(document:click)': 'aussen($event)',
    '(document:keydown.escape)': 'offen.set(false)',
  },
})
export class GlockeComponent {
  protected readonly notifications = inject(NotificationsService);
  private readonly router = inject(Router);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

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

  protected async oeffnen(item: SouffleurNotification): Promise<void> {
    this.offen.set(false);
    await this.notifications.markRead(item);
    if (item.link) {
      await this.router.navigateByUrl(item.link);
    }
  }
}
