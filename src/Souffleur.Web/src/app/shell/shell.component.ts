import { Component, OnInit, computed, inject } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AuthService } from '../core/auth.service';
import { HouseService } from '../core/house.service';
import { LayoutService } from '../core/layout.service';
import { ModulesService } from '../core/modules.service';
import { ThemeService } from '../core/theme.service';
import { ActionsSidebarComponent } from './actions-sidebar.component';
import { ChatSidebarComponent } from './chat-sidebar.component';
import { PreferencesService } from '../core/preferences.service';
import { SortierbarDirective } from '../shared/sortierbar.directive';
import { IconComponent } from '../shared/icon.component';
import { GlockeComponent } from './glocke.component';
import { SucheComponent } from './suche.component';
import { VorfuehrdatenComponent } from './vorfuehrdaten.component';

/**
 * Der Rahmen um alles: oben die Module, links das Handeln, rechts das Reden,
 * in der Mitte die Information.
 */
@Component({
  selector: 'souffleur-shell',
  imports: [
    RouterLink,
    RouterLinkActive,
    RouterOutlet,
    ActionsSidebarComponent,
    ChatSidebarComponent,
    SortierbarDirective,
    IconComponent,
    GlockeComponent,
    SucheComponent,
    VorfuehrdatenComponent,
  ],
  templateUrl: './shell.component.html',
})
export class ShellComponent implements OnInit {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  protected readonly layout = inject(LayoutService);
  protected readonly theme = inject(ThemeService);
  protected readonly modules = inject(ModulesService);
  protected readonly house = inject(HouseService);
  private readonly preferences = inject(PreferencesService);

  /**
   * Die Reiter in der Reihenfolge, die diese Person sich eingerichtet hat.
   * Ein neu eingeschaltetes Modul steht hinten -- es verschwindet nicht,
   * nur weil es beim letzten Sortieren noch nicht da war.
   */
  protected readonly reiter = computed(() =>
    this.preferences.sortiere('module', this.modules.tabs(), (tab) => tab.key),
  );

  protected reiterSortiert(ids: readonly string[]): void {
    void this.preferences.merke('module', ids);
  }

  /**
   * Der Weg an der Modulleiste vorbei. Ohne ihn sind es mit der Tastatur bei
   * jedem Seitenwechsel ein Dutzend Sprünge bis zum ersten Inhalt.
   */
  protected zumInhalt(): void {
    document.getElementById('inhalt')?.focus();
  }

  async ngOnInit(): Promise<void> {
    await this.house.load();
  }
  protected readonly user = this.auth.user;

  protected readonly themeLabels: Record<string, string> = {
    system: 'Erscheinungsbild folgt dem System',
    light: 'Erscheinungsbild hell',
    dark: 'Erscheinungsbild dunkel',
  };

  protected async signOut(): Promise<void> {
    await this.auth.signOut();
    await this.router.navigate(['/anmelden']);
  }
}
