import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { NotificationSetting, NotificationsService } from '../../core/notifications.service';
import { MeldungComponent } from '../../shared/meldung.component';
import { ActionsRegistry } from '../../shell/actions.registry';
import { einstellungsBereiche } from './bereiche';
import { AuthService } from '../../core/auth.service';

/**
 * Wofür jemand eine Mail bekommt. Persönlich, nicht für das Haus: Jede
 * Person stellt es für sich ein, und die Glocke zeigt ohnehin alles.
 */
@Component({
  selector: 'souffleur-benachrichtigungen',
  imports: [FormsModule, MeldungComponent],
  templateUrl: './benachrichtigungen.component.html',
})
export class BenachrichtigungenComponent implements OnInit {
  private readonly notifications = inject(NotificationsService);
  private readonly actions = inject(ActionsRegistry);
  private readonly auth = inject(AuthService);

  protected readonly einstellungen = signal<NotificationSetting[]>([]);
  protected readonly mailBereit = signal(true);
  protected readonly gespeichert = signal(false);
  protected readonly problems = signal<string[]>([]);

  protected readonly arten = [
    { wert: 0, label: 'keine Mail' },
    { wert: 1, label: 'sofort' },
    { wert: 2, label: 'täglich gesammelt' },
  ];

  async ngOnInit(): Promise<void> {
    this.actions.set('Benachrichtigungen', [], einstellungsBereiche(this.auth.can('AccountsManage')));
    const result = await this.notifications.settings();
    this.einstellungen.set(result.settings);
    this.mailBereit.set(result.mailReady);
  }

  /** Gespeichert wird bei jeder Änderung -- ein Knopf dafür wäre eine Gelegenheit, es zu vergessen. */
  protected async aendern(setting: NotificationSetting, email: number): Promise<void> {
    this.einstellungen.update((alle) => alle.map((s) => (s.kind === setting.kind ? { ...s, email } : s)));
    this.gespeichert.set(false);
    try {
      await this.notifications.saveSettings(this.einstellungen().map((s) => ({ kind: s.kind, email: s.email })));
      this.gespeichert.set(true);
    } catch {
      this.problems.set(['Die Einstellung ließ sich nicht speichern.']);
    }
  }
}
