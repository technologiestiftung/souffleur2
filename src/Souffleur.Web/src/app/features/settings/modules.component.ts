import { Component, OnInit, inject, signal } from '@angular/core';
import { ModuleInfo, ModulesService } from '../../core/modules.service';
import { ActionsRegistry } from '../../shell/actions.registry';
import { einstellungsBereiche } from './bereiche';
import { MeldungComponent } from '../../shared/meldung.component';
import { AuthService } from '../../core/auth.service';

/**
 * Was dieses Haus nutzt -- und was nicht.
 *
 * Ein abgeschaltetes Modul verschwindet nicht nur aus den Reitern: Seine
 * Schnittstellen antworten mit 404, und die Kennzahlen dazu fallen weg. Das
 * ist der Zweck (ADR-0004): Ein Haus ohne eigenen Kartenverkauf soll nicht
 * dauernd an einem leeren Ticketing vorbeilaufen.
 */
@Component({
  selector: 'souffleur-modules',
  imports: [MeldungComponent],
  templateUrl: './modules.component.html',
})
export class ModulesComponent implements OnInit {
  private readonly modules = inject(ModulesService);
  private readonly actions = inject(ActionsRegistry);
  private readonly auth = inject(AuthService);

  protected readonly all = signal<ModuleInfo[]>([]);
  protected readonly busy = signal<string | null>(null);
  protected readonly problem = signal('');

  async ngOnInit(): Promise<void> {
    this.actions.set('Einstellungen', [], einstellungsBereiche(this.auth.can('AccountsManage')));

    await this.reload();
  }

  protected async toggle(module: ModuleInfo): Promise<void> {
    this.busy.set(module.key);
    this.problem.set('');

    try {
      await this.modules.set(module.module, !module.isEnabled);
      await this.reload();
    } catch {
      this.problem.set(`${module.label} ließ sich nicht umstellen.`);
    } finally {
      this.busy.set(null);
    }
  }

  private async reload(): Promise<void> {
    await this.modules.load();
    this.all.set(this.modules.all());
  }
}
