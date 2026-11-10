import { Component, OnInit, computed, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { CatalogEntry, CatalogInfo, CatalogsService } from '../../core/catalogs.service';
import { PreferencesService } from '../../core/preferences.service';
import { SortierbarDirective } from '../../shared/sortierbar.directive';
import { DialogComponent } from '../../shared/dialog.component';
import { ActionsRegistry } from '../../shell/actions.registry';
import { AuthService } from '../../core/auth.service';
import { IconComponent } from '../../shared/icon.component';
import { einstellungsBereiche } from './bereiche';
import { messagesFrom } from '../calendar/event-dialog.component';
import { MeldungComponent } from '../../shared/meldung.component';

/**
 * Die Wertelisten des Hauses verwalten -- Terminkategorien, Aufgabenarten,
 * Kostenarten. Siehe ADR-0005.
 *
 * Eine Oberfläche für alle Listen, nicht eine je Liste: Es sind dieselben
 * Felder und dieselben Regeln. Was eine Liste besonders macht -- Farbe,
 * Untergliederung -- meldet der Server.
 */
@Component({
  selector: 'souffleur-catalogs',
  imports: [
    FormsModule,
    RouterLink,
    DialogComponent,
    MeldungComponent,
    SortierbarDirective,
    IconComponent,
  ],
  templateUrl: './catalogs.component.html',
})
export class CatalogsComponent implements OnInit {
  private readonly catalogs = inject(CatalogsService);
  private readonly actions = inject(ActionsRegistry);
  private readonly router = inject(Router);
  private readonly auth = inject(AuthService);

  /** Aus der Route; leer heißt: die erste Liste. */
  readonly catalog = input<string>('');

  private readonly preferences = inject(PreferencesService);

  protected readonly all = signal<CatalogInfo[]>([]);

  /** Dieselbe Liste in der Reihenfolge, die diese Person sich eingerichtet hat. */
  protected readonly listen = computed(() =>
    this.preferences.sortiere('listen', this.all(), (info) => info.key),
  );

  protected listenSortiert(keys: readonly string[]): void {
    void this.preferences.merke('listen', keys);
  }
  protected readonly current = signal<CatalogInfo | null>(null);
  protected readonly entries = signal<CatalogEntry[]>([]);
  protected readonly problems = signal<string[]>([]);
  protected readonly busy = signal(false);

  protected readonly editing = signal<string | null>(null);
  protected readonly draft = signal<Partial<CatalogEntry>>({});

  protected readonly heading = computed(() =>
    this.editing() === 'neu' ? 'Eintrag anlegen' : 'Eintrag ändern',
  );

  async ngOnInit(): Promise<void> {
    this.actions.set('Einstellungen', [], einstellungsBereiche(this.auth.can('AccountsManage')));

    const catalogs = await this.catalogs.loadCatalogs();
    this.all.set(catalogs);

    const wanted = catalogs.find((c) => c.key === this.catalog()) ?? catalogs[0];
    if (wanted) {
      await this.select(wanted);
    }
  }

  protected async select(info: CatalogInfo): Promise<void> {
    this.current.set(info);
    this.editing.set(null);
    this.problems.set([]);
    await this.router.navigate(['/einstellungen/listen', info.key], { replaceUrl: true });
    await this.reload();
  }

  protected startNew(): void {
    this.problems.set([]);
    this.editing.set('neu');
    this.draft.set({ label: '', color: this.current()?.usesColor ? '#6b2c45' : null });
  }

  protected startEdit(entry: CatalogEntry): void {
    this.problems.set([]);
    this.editing.set(entry.id);
    this.draft.set({ ...entry });
  }

  protected cancel(): void {
    this.editing.set(null);
    this.draft.set({});
  }

  protected patch(change: Partial<CatalogEntry>): void {
    this.draft.update((current) => ({ ...current, ...change }));
  }

  protected async save(): Promise<void> {
    const info = this.current();
    const draft = this.draft();
    if (!info || !draft.label?.trim()) {
      this.problems.set(['Eine Beschriftung ist nötig.']);
      return;
    }

    this.busy.set(true);
    this.problems.set([]);

    const input = {
      label: draft.label.trim(),
      code: draft.code?.trim() || null,
      description: draft.description?.trim() || null,
      color: info.usesColor ? (draft.color ?? null) : null,
      parentId: draft.parentId ?? null,
    };

    try {
      if (this.editing() === 'neu') {
        await this.catalogs.create(info.key, input);
      } else {
        await this.catalogs.update(info.key, this.editing()!, input);
      }

      this.editing.set(null);
      await this.reload();
    } catch (error) {
      this.problems.set(messagesFrom(error));
    } finally {
      this.busy.set(false);
    }
  }

  protected async setActive(entry: CatalogEntry, isActive: boolean): Promise<void> {
    const info = this.current();
    if (!info) {
      return;
    }

    await this.catalogs.setActive(info.key, entry.id, isActive);
    await this.reload();
  }

  protected async remove(entry: CatalogEntry): Promise<void> {
    const info = this.current();
    if (!info) {
      return;
    }

    this.problems.set([]);

    try {
      await this.catalogs.remove(info.key, entry.id);
      await this.reload();
    } catch (error) {
      this.problems.set(messagesFrom(error));
    }
  }

  /** Einen Eintrag in der Reihenfolge des Hauses verschieben. */
  /** Vom Griff oder der Tastatur: Die Liste folgt sofort, der Server danach. */
  protected async umsortiert(ids: readonly string[]): Promise<void> {
    const info = this.current();
    if (!info) {
      return;
    }

    const vorhanden = new Map(this.entries().map((e) => [e.id, e]));
    this.entries.set(ids.map((id) => vorhanden.get(id)).filter((e) => e !== undefined));
    await this.catalogs.reorder(info.key, [...ids]);
    await this.reload();
  }

  private async reload(): Promise<void> {
    const info = this.current();
    if (info) {
      this.entries.set(await this.catalogs.entries(info.key, true));
    }
  }
}
