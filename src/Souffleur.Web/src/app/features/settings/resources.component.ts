import { DestroyRef, Component, OnInit, computed, inject, signal } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { verfolgeAdresse } from '../../shared/sprungziel';
import { FormsModule } from '@angular/forms';
import { CatalogEntry, CatalogsService } from '../../core/catalogs.service';
import { Resource, ResourcesService } from '../../core/resources.service';
import { DialogComponent } from '../../shared/dialog.component';
import { ActionsRegistry } from '../../shell/actions.registry';
import { messagesFrom } from '../calendar/event-dialog.component';
import { MeldungComponent } from '../../shared/meldung.component';
import { IconComponent } from '../../shared/icon.component';
import { SortierbarDirective } from '../../shared/sortierbar.directive';
import { AuthService } from '../../core/auth.service';
import { einstellungsBereiche } from './bereiche';

/**
 * Die Ressourcen des Hauses verwalten -- Bühnen, Räume, Werkstätten,
 * Fahrzeuge.
 *
 * Eigene Oberfläche und nicht die der Listen (ADR-0005): Eine Ressource trägt
 * mehr als ihren Namen. Ihre *Art* dagegen ist eine Liste wie jede andere und
 * wird dort gepflegt.
 */
@Component({
  selector: 'souffleur-resources',
  imports: [FormsModule, DialogComponent, MeldungComponent, IconComponent, SortierbarDirective],
  templateUrl: './resources.component.html',
})
export class ResourcesComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly destroyRef = inject(DestroyRef);
  private readonly resources = inject(ResourcesService);
  private readonly catalogs = inject(CatalogsService);
  private readonly actions = inject(ActionsRegistry);
  private readonly auth = inject(AuthService);

  protected readonly entries = signal<Resource[]>([]);
  protected readonly kinds = signal<CatalogEntry[]>([]);
  protected readonly problems = signal<string[]>([]);
  protected readonly busy = signal(false);

  protected readonly editing = signal<string | null>(null);
  protected readonly draft = signal<Partial<Resource>>({});

  protected readonly heading = computed(() =>
    this.editing() === 'neu' ? 'Ressource anlegen' : 'Ressource ändern',
  );

  async ngOnInit(): Promise<void> {
    this.actions.set(
      'Einstellungen',
      [
        {
          label: 'Ressource anlegen',
          icon: 'ressource',
          primary: true,
          run: () => this.startNew(),
        },
      ],
      [
        ...einstellungsBereiche(this.auth.can('AccountsManage')),
        {
          label: 'Arten von Ressourcen',
          icon: 'etikett',
          path: '/einstellungen/listen/ResourceKind',
        },
      ],
    );

    this.kinds.set(await this.catalogs.entries('ResourceKind'));
    await this.reload();

    // Aus der Suche: `?ressource=<id>` öffnet den Bogen.
    verfolgeAdresse(this.route, this.destroyRef, (params) => {
      const id = params.get('ressource');
      const resource = id ? this.entries().find((r) => r.id === id) : undefined;
      if (resource) {
        this.startEdit(resource);
      }
    });
  }

  protected startNew(): void {
    this.problems.set([]);
    this.editing.set('neu');
    this.draft.set({ name: '', isExclusive: true });
  }

  protected startEdit(resource: Resource): void {
    this.problems.set([]);
    this.editing.set(resource.id);
    this.draft.set({ ...resource });
  }

  protected cancel(): void {
    this.editing.set(null);
    this.draft.set({});
  }

  protected patch(change: Partial<Resource>): void {
    this.draft.update((current) => ({ ...current, ...change }));
  }

  protected async save(): Promise<void> {
    const draft = this.draft();
    if (!draft.name?.trim()) {
      this.problems.set(['Ein Name ist nötig.']);
      return;
    }

    this.busy.set(true);
    this.problems.set([]);

    const input = {
      name: draft.name.trim(),
      shortName: draft.shortName?.trim() || null,
      kindId: draft.kindId || null,
      description: draft.description?.trim() || null,
      capacity: draft.capacity ?? null,
      isExclusive: draft.isExclusive ?? true,
    };

    try {
      if (this.editing() === 'neu') {
        await this.resources.create(input);
      } else {
        await this.resources.update(this.editing()!, input);
      }

      this.editing.set(null);
      await this.reload();
    } catch (error) {
      this.problems.set(messagesFrom(error));
    } finally {
      this.busy.set(false);
    }
  }

  protected async setActive(resource: Resource, isActive: boolean): Promise<void> {
    await this.resources.setActive(resource.id, isActive);
    await this.reload();
  }

  protected async remove(resource: Resource): Promise<void> {
    this.problems.set([]);

    try {
      await this.resources.remove(resource.id);
      await this.reload();
    } catch (error) {
      this.problems.set(messagesFrom(error));
    }
  }

  /** Vom Griff oder der Tastatur: Die Liste folgt sofort, der Server danach. */
  protected async umsortiert(ids: readonly string[]): Promise<void> {
    const vorhanden = new Map(this.entries().map((r) => [r.id, r]));
    this.entries.set(ids.map((id) => vorhanden.get(id)).filter((r) => r !== undefined));
    await this.resources.reorder([...ids]);
    await this.reload();
  }

  protected kindLabel(id: string | null | undefined): string {
    return this.kinds().find((kind) => kind.id === id)?.label ?? '—';
  }

  private async reload(): Promise<void> {
    this.entries.set(await this.resources.load(true));
  }
}
