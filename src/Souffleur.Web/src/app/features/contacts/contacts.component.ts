import { DestroyRef, Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { verfolgeAdresse } from '../../shared/sprungziel';
import {
  Contact,
  ContactInput,
  ContactKind,
  ContactsService,
  RoleInfo,
  UserAccount,
} from '../../core/contacts.service';
import { ModulesService } from '../../core/modules.service';
import { Casting, Production, ProductionsService } from '../../core/productions.service';
import { DialogComponent } from '../../shared/dialog.component';
import { LesezeichenComponent } from '../../shared/lesezeichen.component';
import { BookmarkDraft, BookmarksService } from '../../core/bookmarks.service';
import { MetricStripComponent } from '../../shared/metric-strip.component';
import { ActionsRegistry } from '../../shell/actions.registry';
import { messagesFrom } from '../calendar/event-dialog.component';
import { MeldungComponent } from '../../shared/meldung.component';

/** Der Bogen, solange er offen ist. Als lose Felder, nicht als halber Kontakt. */
interface Draft {
  kind: number;
  roles: number[];
  name: string;
  givenName: string;
  surname: string;
  position: string;
  email: string;
  phone: string;
  street: string;
  postalCode: string;
  city: string;
  notes: string;
  iban: string;
  bic: string;
  paymentTermDays: string;
  mayBeContacted: boolean;
  userId: string;
}

function emptyDraft(): Draft {
  return {
    kind: ContactKind.Organization,
    roles: [],
    name: '',
    givenName: '',
    surname: '',
    position: '',
    email: '',
    phone: '',
    street: '',
    postalCode: '',
    city: '',
    notes: '',
    iban: '',
    bic: '',
    paymentTermDays: '',
    userId: '',
    mayBeContacted: false,
  };
}

@Component({
  selector: 'souffleur-contacts',
  imports: [FormsModule, RouterLink, MetricStripComponent, DialogComponent,
    LesezeichenComponent, MeldungComponent],
  templateUrl: './contacts.component.html',
})
export class ContactsComponent implements OnInit {
  private readonly contactsApi = inject(ContactsService);
  private readonly productions = inject(ProductionsService);
  private readonly modules = inject(ModulesService);
  private readonly actions = inject(ActionsRegistry);
  private readonly bookmarks = inject(BookmarksService);

  /** Lesezeichen, die beim Anlegen gesammelt werden -- den Kontakt gibt es noch nicht. */
  protected readonly pendingBookmarks = signal<BookmarkDraft[]>([]);
  private readonly route = inject(ActivatedRoute);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly contacts = signal<Contact[]>([]);
  protected readonly roles = signal<RoleInfo[]>([]);

  /**
   * Die Anmeldekonten. Leer, wenn das eigene Konto sie nicht sehen darf --
   * dann erscheint die Auswahl gar nicht erst.
   */
  protected readonly accounts = signal<UserAccount[]>([]);
  protected readonly loading = signal(true);

  protected readonly search = signal('');
  protected readonly roleFilter = signal<number | null>(null);

  /** Offen, solange jemand einen Kontakt schreibt. */
  protected readonly editing = signal<Contact | null>(null);
  protected readonly open = signal(false);
  protected readonly draft = signal<Draft>(emptyDraft());
  protected readonly problems = signal<string[]>([]);
  protected readonly busy = signal(false);

  protected readonly kinds = ContactKind;

  /**
   * In welchen Produktionen der Kontakt besetzt ist -- und was zur Wahl
   * steht, um ihn in eine weitere zu setzen. Leer, wenn das Modul aus ist.
   */
  protected readonly castings = signal<Casting[]>([]);
  protected readonly productionChoices = signal<Production[]>([]);
  protected readonly newCasting = signal({ productionId: '', roleName: '' });
  protected readonly productionsOn = computed(() => this.modules.isEnabled('Production'));

  protected readonly heading = computed(() => {
    const current = this.editing();
    return current ? `${current.name} ändern` : 'Neuer Kontakt';
  });

  protected readonly shown = computed(() => {
    const term = this.search().trim().toLowerCase();
    const role = this.roleFilter();
    return this.contacts().filter((c) => {
      const matchesRole = role === null || c.roles.some((r) => r.role === role);
      const matchesTerm =
        !term ||
        c.name.toLowerCase().includes(term) ||
        (c.email ?? '').toLowerCase().includes(term) ||
        (c.city ?? '').toLowerCase().includes(term);
      return matchesRole && matchesTerm;
    });
  });

  /** Wie viele Kontakte je Rolle -- für die Filterleiste. */
  protected readonly counts = computed(() => {
    const result = new Map<number, number>();
    for (const contact of this.contacts()) {
      for (const role of contact.roles) {
        result.set(role.role, (result.get(role.role) ?? 0) + 1);
      }
    }
    return result;
  });

  /**
   * Ob dieser Kontakt ein Anmeldekonto tragen kann: eine Person, die zum Haus
   * gehört. Eine Druckerei meldet sich nicht an.
   */
  protected readonly wantsAccount = computed(() => {
    const staff = this.roles().find((role) => role.key === 'Staff');

    return (
      this.accounts().length > 0 &&
      this.draft().kind === ContactKind.Person &&
      staff !== undefined &&
      this.draft().roles.includes(staff.role)
    );
  });

  /** Ob eine der gewählten Rollen eine Bankverbindung erwartet. */
  protected readonly wantsBank = computed(() => {
    const chosen = this.draft().roles;
    return this.roles().some((role) => role.needsBankDetails && chosen.includes(role.role));
  });

  async ngOnInit(): Promise<void> {
    this.actions.set('Kontakte', [
      { label: 'Kontakt anlegen', icon: 'plus', primary: true, run: () => this.startNew() },
    ]);

    const [contacts, roles] = await Promise.all([
      this.contactsApi.list(),
      this.contactsApi.roles(),
    ]);

    this.contacts.set(contacts);
    this.roles.set(roles);

    try {
      this.accounts.set(await this.contactsApi.accounts());
    } catch {
      // Nur die Verwaltung sieht die Konten. Für alle anderen bleibt die
      // Auswahl weg, statt mit einem Fehler aufzufallen.
      this.accounts.set([]);
    }
    this.loading.set(false);

    // Wer von der Übersicht kommt, will den Bogen schon offen sehen.
    if (this.route.snapshot.queryParamMap.get('neu')) {
      this.startNew();
    }

    // Aus der Suche: `?kontakt=<id>` öffnet den Bogen.
    verfolgeAdresse(this.route, this.destroyRef, (params) => {
      const id = params.get('kontakt');
      const contact = id ? this.contacts().find((c) => c.id === id) : undefined;
      if (contact) {
        this.startEdit(contact);
      }
    });
  }

  protected countFor(role: number): number {
    return this.counts().get(role) ?? 0;
  }

  protected toggleRole(role: number): void {
    this.roleFilter.update((current) => (current === role ? null : role));
  }

  protected startNew(): void {
    this.problems.set([]);
    this.editing.set(null);
    this.draft.set(emptyDraft());
    this.open.set(true);
  }

  protected startEdit(contact: Contact): void {
    this.problems.set([]);
    this.editing.set(contact);
    void this.loadCastings(contact);
    this.draft.set({
      kind: contact.kind,
      roles: contact.roles.map((r) => r.role),
      name: contact.name,
      givenName: contact.givenName ?? '',
      surname: contact.surname ?? '',
      position: contact.position ?? '',
      email: contact.email ?? '',
      phone: contact.phone ?? '',
      street: contact.street ?? '',
      postalCode: contact.postalCode ?? '',
      city: contact.city ?? '',
      notes: contact.notes ?? '',
      iban: contact.bankDetails?.iban ?? '',
      bic: contact.bankDetails?.bic ?? '',
      paymentTermDays: contact.bankDetails?.paymentTermDays?.toString() ?? '',
      mayBeContacted: contact.mayBeContacted,
      userId: contact.userId ?? '',
    });
    this.open.set(true);
  }

  protected cancel(): void {
    this.open.set(false);
    this.editing.set(null);
    this.problems.set([]);
  }

  // --- Produktionen: wo diese Person besetzt ist ---

  private async loadCastings(contact: Contact): Promise<void> {
    this.castings.set([]);
    this.newCasting.set({ productionId: '', roleName: '' });
    if (!this.productionsOn() || contact.kind !== ContactKind.Person) {
      return;
    }
    const [castings, choices] = await Promise.all([
      this.productions.castings(contact.id),
      this.productions.choices(),
    ]);
    // Zwischenzeitlich einen anderen Bogen geöffnet? Dann gehört das nicht hierher.
    if (this.editing()?.id === contact.id) {
      this.castings.set(castings);
      this.productionChoices.set(choices);
    }
  }

  protected patchCasting(change: Partial<{ productionId: string; roleName: string }>): void {
    this.newCasting.update((current) => ({ ...current, ...change }));
  }

  /** Eine Rolle in einer Produktion -- vom Kontakt aus eingetragen. */
  protected async addCasting(contact: Contact): Promise<void> {
    const { productionId, roleName } = this.newCasting();
    if (!productionId) {
      return;
    }
    this.busy.set(true);
    this.problems.set([]);
    try {
      await this.productions.addRole(productionId, {
        // Ohne Rollennamen heißt die Zeile „Mitwirkung“ -- umbenennen geht im Buch.
        name: roleName.trim() || 'Mitwirkung',
        contactId: contact.id,
      });
      await this.loadCastings(contact);
    } catch (error) {
      this.problems.set(messagesFrom(error));
    } finally {
      this.busy.set(false);
    }
  }

  protected async removeCasting(contact: Contact, casting: Casting): Promise<void> {
    this.busy.set(true);
    this.problems.set([]);
    try {
      await this.productions.removeRole(casting.productionId, casting.roleId);
      await this.loadCastings(contact);
    } catch (error) {
      this.problems.set(messagesFrom(error));
    } finally {
      this.busy.set(false);
    }
  }

  protected patch(change: Partial<Draft>): void {
    this.draft.update((current) => ({ ...current, ...change }));
  }

  protected toggleDraftRole(role: number): void {
    this.draft.update((current) => ({
      ...current,
      roles: current.roles.includes(role)
        ? current.roles.filter((r) => r !== role)
        : [...current.roles, role],
    }));
  }

  protected hasRole(role: number): boolean {
    return this.draft().roles.includes(role);
  }

  protected async save(): Promise<void> {
    const draft = this.draft();
    const isPerson = draft.kind === ContactKind.Person;

    // Dieselben Regeln wie auf dem Server, nur früher: Wer tippt, soll die
    // Antwort nicht erst nach dem Absenden bekommen.
    const problems: string[] = [];
    if (isPerson && !draft.surname.trim()) {
      problems.push('Eine Person braucht einen Nachnamen.');
    }
    if (!isPerson && !draft.name.trim()) {
      problems.push('Eine Organisation braucht einen Namen.');
    }
    if (draft.roles.length === 0) {
      problems.push('Ein Kontakt braucht mindestens eine Rolle.');
    }
    if (problems.length > 0) {
      this.problems.set(problems);
      return;
    }

    const existing = this.editing();
    const input: ContactInput = {
      kind: draft.kind,
      roles: draft.roles,
      name: isPerson ? null : draft.name.trim(),
      givenName: isPerson ? draft.givenName.trim() || null : null,
      surname: isPerson ? draft.surname.trim() : null,
      position: draft.position.trim() || null,
      email: draft.email.trim() || null,
      phone: draft.phone.trim() || null,
      street: draft.street.trim() || null,
      postalCode: draft.postalCode.trim() || null,
      city: draft.city.trim() || null,
      notes: draft.notes.trim() || null,

      // Eine einmal erteilte Einwilligung behält ihr Datum; sie neu zu
      // stempeln sähe aus, als wäre heute zugestimmt worden.
      contactConsentAt: draft.mayBeContacted
        ? (existing?.contactConsentAt ?? new Date().toISOString())
        : null,
      userId: draft.userId || null,
      bankDetails: draft.iban.trim()
        ? {
            iban: draft.iban.trim(),
            bic: draft.bic.trim() || null,
            paymentTermDays: draft.paymentTermDays ? Number(draft.paymentTermDays) : null,
          }
        : null,
    };

    this.busy.set(true);
    this.problems.set([]);

    try {
      if (existing) {
        await this.contactsApi.update(existing.id, input);
      } else {
        const created = await this.contactsApi.create(input);
        for (const draft of this.pendingBookmarks()) {
          await this.bookmarks.add('Contact', created.id, draft);
        }
      }
      this.pendingBookmarks.set([]);

      this.contacts.set(await this.contactsApi.list());
      this.open.set(false);
      this.editing.set(null);
    } catch (error) {
      this.problems.set(messagesFrom(error));
    } finally {
      this.busy.set(false);
    }
  }

  protected async deactivate(contact: Contact): Promise<void> {
    this.busy.set(true);
    try {
      await this.contactsApi.deactivate(contact.id);
      this.contacts.set(await this.contactsApi.list());
    } catch (error) {
      this.problems.set(messagesFrom(error));
    } finally {
      this.busy.set(false);
    }
  }
}
