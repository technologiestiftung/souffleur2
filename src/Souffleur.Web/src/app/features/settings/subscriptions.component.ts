import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { CatalogEntry, CatalogsService } from '../../core/catalogs.service';
import { Person, PeopleService } from '../../core/people.service';
import { Resource, ResourcesService } from '../../core/resources.service';
import { Subscription, SubscriptionsService, webcal } from '../../core/subscriptions.service';
import { DialogComponent } from '../../shared/dialog.component';
import { ActionsRegistry } from '../../shell/actions.registry';
import { einstellungsBereiche } from './bereiche';
import { AuthService } from '../../core/auth.service';
import { messagesFrom } from '../calendar/event-dialog.component';
import { MeldungComponent } from '../../shared/meldung.component';

/**
 * Die Adressen, unter denen der Kalender abonniert werden kann.
 *
 * Die Seite macht zwei Dinge sichtbar, die sonst niemand ahnt: dass die
 * Adresse für sich allein Zugang gibt, und dass sie sich zurückziehen lässt.
 */
@Component({
  selector: 'souffleur-subscriptions',
  imports: [FormsModule, DialogComponent, MeldungComponent],
  templateUrl: './subscriptions.component.html',
})
export class SubscriptionsComponent implements OnInit {
  private readonly subscriptions = inject(SubscriptionsService);
  private readonly catalogs = inject(CatalogsService);
  private readonly resources = inject(ResourcesService);
  private readonly peopleList = inject(PeopleService);
  private readonly actions = inject(ActionsRegistry);
  private readonly auth = inject(AuthService);

  protected readonly entries = signal<Subscription[]>([]);
  protected readonly categories = signal<CatalogEntry[]>([]);
  protected readonly rooms = signal<Resource[]>([]);
  protected readonly people = signal<Person[]>([]);
  protected readonly problems = signal<string[]>([]);
  protected readonly busy = signal(false);

  protected readonly creating = signal(false);
  protected readonly name = signal('');
  protected readonly pickedCategories = signal<ReadonlySet<string>>(new Set());
  protected readonly pickedResources = signal<ReadonlySet<string>>(new Set());

  /**
   * Damit aus dem Spielplan ein Dienstplan wird: Ein Abonnement auf die
   * eigene Person zeigt im Telefon genau die Termine, zu denen man kommen
   * muss.
   */
  protected readonly pickedPeople = signal<ReadonlySet<string>>(new Set());
  protected readonly includeCancelled = signal(true);

  /** Welche Adresse gerade im Klartext steht. */
  protected readonly shown = signal<string | null>(null);
  protected readonly copied = signal<string | null>(null);

  protected readonly webcal = webcal;

  /**
   * Ob die Adresse verschlüsselt erreichbar ist.
   *
   * Apple Kalender und Outlook lösen `webcal:` zu `https:` auf. Über eine
   * unverschlüsselte Adresse -- wie in der Entwicklungsumgebung -- scheitert
   * der Knopf deshalb, bevor die Anfrage den Server erreicht. Dann ist das
   * Einfügen von Hand der einzige Weg, und das muss dastehen.
   */
  protected readonly secure = typeof location === 'undefined' || location.protocol === 'https:';

  async ngOnInit(): Promise<void> {
    this.actions.set(
      'Einstellungen',
      [
        {
          label: 'Abonnement anlegen',
          icon: 'kalender',
          primary: true,
          run: () => this.startNew(),
        },
      ],
      einstellungsBereiche(this.auth.can('AccountsManage')),
    );

    this.categories.set(await this.catalogs.entries('EventCategory'));
    this.rooms.set(await this.resources.load());
    this.people.set(await this.peopleList.load());
    await this.reload();
  }

  protected startNew(): void {
    this.problems.set([]);
    this.creating.set(true);
    this.name.set('');
    this.pickedCategories.set(new Set());
    this.pickedResources.set(new Set());
    this.pickedPeople.set(new Set());
    this.includeCancelled.set(true);
  }

  protected cancel(): void {
    this.creating.set(false);
  }

  protected toggleCategory(id: string): void {
    this.pickedCategories.update((current) => toggle(current, id));
  }

  protected toggleResource(id: string): void {
    this.pickedResources.update((current) => toggle(current, id));
  }

  protected isCategory(id: string): boolean {
    return this.pickedCategories().has(id);
  }

  protected isResource(id: string): boolean {
    return this.pickedResources().has(id);
  }

  protected togglePerson(id: string): void {
    this.pickedPeople.update((current) => toggle(current, id));
  }

  protected isPerson(id: string): boolean {
    return this.pickedPeople().has(id);
  }

  protected async save(): Promise<void> {
    if (!this.name().trim()) {
      this.problems.set(['Ein Name ist nötig.']);
      return;
    }

    this.busy.set(true);
    this.problems.set([]);

    try {
      const created = await this.subscriptions.create({
        name: this.name().trim(),
        categoryIds: [...this.pickedCategories()],
        resourceIds: [...this.pickedResources()],
        contactIds: [...this.pickedPeople()],
        includeCancelled: this.includeCancelled(),
      });

      this.creating.set(false);

      // Die neue Adresse gleich offen zeigen -- dafür legt man sie an.
      this.shown.set(created.id);
      await this.reload();
    } catch (error) {
      this.problems.set(messagesFrom(error));
    } finally {
      this.busy.set(false);
    }
  }

  protected async setActive(entry: Subscription, isActive: boolean): Promise<void> {
    await this.subscriptions.update(entry.id, { name: entry.name, isActive });
    await this.reload();
  }

  protected async rotate(entry: Subscription): Promise<void> {
    const renewed = await this.subscriptions.rotate(entry.id);
    this.shown.set(renewed.id);
    await this.reload();
  }

  protected async remove(entry: Subscription): Promise<void> {
    this.problems.set([]);

    try {
      await this.subscriptions.remove(entry.id);
      await this.reload();
    } catch (error) {
      this.problems.set(messagesFrom(error));
    }
  }

  protected reveal(entry: Subscription): void {
    this.shown.update((current) => (current === entry.id ? null : entry.id));
  }

  protected async copy(entry: Subscription): Promise<void> {
    try {
      await navigator.clipboard.writeText(entry.address);
      this.copied.set(entry.id);
    } catch {
      // Ohne Zwischenablage bleibt der Weg über das Feld -- es steht offen da.
      this.shown.set(entry.id);
    }
  }

  /** Was das Abonnement zeigt, in einem Satz. */
  protected scope(entry: Subscription): string {
    const parts: string[] = [];

    if (entry.categoryIds.length > 0) {
      parts.push(
        entry.categoryIds
          .map((id) => this.categories().find((c) => c.id === id)?.label ?? '?')
          .join(', '),
      );
    }

    if (entry.resourceIds.length > 0) {
      parts.push(
        entry.resourceIds
          .map((id) => this.rooms().find((r) => r.id === id)?.name ?? '?')
          .join(', '),
      );
    }

    if (entry.contactIds.length > 0) {
      parts.push(
        entry.contactIds
          .map((id) => this.people().find((p) => p.id === id)?.name ?? '?')
          .join(', '),
      );
    }

    if (parts.length === 0) {
      parts.push('alles');
    }

    if (!entry.includeCancelled) {
      parts.push('ohne Abgesagtes');
    }

    return parts.join(' · ');
  }

  protected fetched(entry: Subscription): string {
    if (!entry.lastFetchedAt) {
      return 'noch nie abgerufen';
    }

    const when = new Date(entry.lastFetchedAt).toLocaleString('de-DE', {
      day: 'numeric',
      month: 'numeric',
      hour: '2-digit',
      minute: '2-digit',
    });

    return `${when} · ${entry.fetchCount}×`;
  }

  private async reload(): Promise<void> {
    this.entries.set(await this.subscriptions.load());
  }
}

function toggle(current: ReadonlySet<string>, id: string): ReadonlySet<string> {
  const next = new Set(current);
  if (!next.delete(id)) {
    next.add(id);
  }
  return next;
}
