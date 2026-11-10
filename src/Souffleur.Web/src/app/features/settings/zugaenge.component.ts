import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import {
  AccessGroup,
  AccessRole,
  AccessService,
  AccessUser,
  PermissionInfo,
} from '../../core/access.service';
import { DialogComponent } from '../../shared/dialog.component';
import { IconComponent } from '../../shared/icon.component';
import { MeldungComponent } from '../../shared/meldung.component';
import { ActionsRegistry } from '../../shell/actions.registry';
import { einstellungsBereiche } from './bereiche';

type Reiter = 'personen' | 'gruppen' | 'rollen';

/** Was gerade im Fenster bearbeitet wird. `id: null` heißt: neu. */
type Entwurf =
  | { art: 'rolle'; id: string | null; isBuiltIn: boolean; name: string; description: string; permissions: number[] }
  | {
      art: 'gruppe';
      id: string | null;
      name: string;
      note: string;
      isActive: boolean;
      roleIds: string[];
      memberIds: string[];
    }
  | { art: 'person'; id: string; name: string; roleIds: string[] };

/**
 * Wer was darf.
 *
 * Drei Blicke auf dieselbe Sache, weil man mit drei verschiedenen Fragen
 * hierherkommt: „Was darf Anna?" (Personen), „Wer ist in der Technik?"
 * (Gruppen), „Was heißt eigentlich Buchhaltung?" (Rollen).
 *
 * Rechte addieren sich: Was eine Person darf, ist alles aus ihren eigenen
 * Rollen und aus den Rollen ihrer Gruppen. Darum zeigt die Personenansicht
 * beides -- sonst fragt man sich, woher ein Recht kommt, das nirgends steht.
 */
@Component({
  selector: 'souffleur-zugaenge',
  imports: [FormsModule, DialogComponent, IconComponent, MeldungComponent],
  templateUrl: './zugaenge.component.html',
})
export class ZugaengeComponent implements OnInit {
  private readonly access = inject(AccessService);
  private readonly actions = inject(ActionsRegistry);

  protected readonly reiter = signal<Reiter>('personen');

  protected readonly katalog = signal<PermissionInfo[]>([]);
  protected readonly rollen = signal<AccessRole[]>([]);
  protected readonly gruppen = signal<AccessGroup[]>([]);
  protected readonly personen = signal<AccessUser[]>([]);

  protected readonly problems = signal<string[]>([]);
  protected readonly busy = signal(false);
  protected readonly entwurf = signal<Entwurf | null>(null);

  /** Der Katalog nach Bereichen, in der Reihenfolge, in der er kommt. */
  protected readonly bereiche = computed(() => {
    const nach = new Map<string, PermissionInfo[]>();
    for (const recht of this.katalog()) {
      nach.set(recht.area, [...(nach.get(recht.area) ?? []), recht]);
    }
    return [...nach].map(([area, rechte]) => ({ area, rechte }));
  });

  private readonly rollenNamen = computed(
    () => new Map(this.rollen().map((r) => [r.id, r.label])),
  );

  private readonly personenNamen = computed(
    () => new Map(this.personen().map((p) => [p.id, p.name])),
  );

  protected readonly titel = computed(() => {
    const e = this.entwurf();
    if (!e) {
      return '';
    }
    if (e.art === 'person') {
      return `Rollen von ${e.name}`;
    }
    const was = e.art === 'rolle' ? 'Rolle' : 'Gruppe';
    return e.id === null ? `${was} anlegen` : `${was} ändern`;
  });

  async ngOnInit(): Promise<void> {
    this.actions.set(
      'Zugänge',
      [
        { label: 'Gruppe anlegen', icon: 'gruppe', primary: true, run: () => this.neueGruppe() },
        { label: 'Rolle anlegen', icon: 'rolle', run: () => this.neueRolle() },
      ],
      [
        ...einstellungsBereiche(true),
      ],
    );

    await this.laden();
  }

  private async laden(): Promise<void> {
    try {
      const [katalog, rollen, gruppen, personen] = await Promise.all([
        this.access.permissions(),
        this.access.roles(),
        this.access.groups(),
        this.access.users(),
      ]);
      this.katalog.set(katalog);
      this.rollen.set(rollen);
      this.gruppen.set(gruppen);
      this.personen.set(personen);
    } catch (error) {
      this.problems.set(meldungen(error));
    }
  }

  // ── Anzeige ────────────────────────────────────────────────────────────

  protected rolleName(id: string): string {
    return this.rollenNamen().get(id) ?? '—';
  }

  protected personName(id: string): string {
    return this.personenNamen().get(id) ?? '—';
  }

  protected gruppenVon(person: AccessUser): AccessGroup[] {
    return this.gruppen().filter((g) => g.isActive && g.memberIds.includes(person.id));
  }

  /** Wie viele Rechte jemand insgesamt hat -- eigene und aus Gruppen. */
  protected rechteVon(person: AccessUser): number {
    const rollen = new Set([
      ...person.roleIds,
      ...this.gruppenVon(person).flatMap((g) => g.roleIds),
    ]);
    const rechte = new Set(
      this.rollen()
        .filter((r) => rollen.has(r.id))
        .flatMap((r) => r.permissions),
    );
    return rechte.size;
  }

  // ── Bearbeiten ─────────────────────────────────────────────────────────

  protected neueRolle(): void {
    this.problems.set([]);
    this.reiter.set('rollen');
    this.entwurf.set({ art: 'rolle', id: null, isBuiltIn: false, name: '', description: '', permissions: [] });
  }

  protected rolleBearbeiten(rolle: AccessRole): void {
    this.problems.set([]);
    this.entwurf.set({
      art: 'rolle',
      id: rolle.id,
      isBuiltIn: rolle.isBuiltIn,
      // Mitgelieferte zeigen ihren deutschen Namen; geschickt wird der
      // Schlüssel, und der Server lässt ihn ohnehin stehen.
      name: rolle.isBuiltIn ? rolle.label : rolle.name,
      description: rolle.description ?? '',
      permissions: [...rolle.permissions],
    });
  }

  protected neueGruppe(): void {
    this.problems.set([]);
    this.reiter.set('gruppen');
    this.entwurf.set({
      art: 'gruppe',
      id: null,
      name: '',
      note: '',
      isActive: true,
      roleIds: [],
      memberIds: [],
    });
  }

  protected gruppeBearbeiten(gruppe: AccessGroup): void {
    this.problems.set([]);
    this.entwurf.set({
      art: 'gruppe',
      id: gruppe.id,
      name: gruppe.name,
      note: gruppe.note ?? '',
      isActive: gruppe.isActive,
      roleIds: [...gruppe.roleIds],
      memberIds: [...gruppe.memberIds],
    });
  }

  protected personBearbeiten(person: AccessUser): void {
    this.problems.set([]);
    this.entwurf.set({ art: 'person', id: person.id, name: person.name, roleIds: [...person.roleIds] });
  }

  protected schliessen(): void {
    this.entwurf.set(null);
    this.problems.set([]);
  }

  protected aendern(change: Partial<Entwurf>): void {
    this.entwurf.update((e) => (e ? ({ ...e, ...change } as Entwurf) : e));
  }

  /** Nimmt einen Eintrag in eine Liste auf oder heraus. */
  protected umschalten<T>(liste: readonly T[], eintrag: T): T[] {
    return liste.includes(eintrag) ? liste.filter((x) => x !== eintrag) : [...liste, eintrag];
  }

  /** Ein ganzer Bereich auf einmal -- an, wenn noch nicht alle an sind. */
  protected bereichUmschalten(liste: readonly number[], rechte: readonly PermissionInfo[]): number[] {
    const nummern = rechte.map((r) => r.permission);
    const alleAn = nummern.every((n) => liste.includes(n));
    return alleAn
      ? liste.filter((n) => !nummern.includes(n))
      : [...new Set([...liste, ...nummern])];
  }

  protected alleAn(liste: readonly number[], rechte: readonly PermissionInfo[]): boolean {
    return rechte.every((r) => liste.includes(r.permission));
  }

  /**
   * Einige an, nicht alle. Ohne den Halbzustand zeigte die Bereichsbox
   * „aus", obwohl darunter Haken stehen -- und wer sie anklickt, um
   * auszuschalten, schaltet stattdessen alles an.
   */
  protected teilweise(liste: readonly number[], rechte: readonly PermissionInfo[]): boolean {
    const an = rechte.filter((r) => liste.includes(r.permission)).length;
    return an > 0 && an < rechte.length;
  }

  protected async speichern(): Promise<void> {
    const e = this.entwurf();
    if (!e) {
      return;
    }

    if (e.art !== 'person' && !e.name.trim()) {
      this.problems.set(['Ein Name ist nötig.']);
      return;
    }

    this.busy.set(true);
    try {
      if (e.art === 'rolle') {
        const input = { name: e.name.trim(), description: e.description.trim() || null, permissions: e.permissions };
        await (e.id === null ? this.access.createRole(input) : this.access.updateRole(e.id, input));
      } else if (e.art === 'gruppe') {
        const input = {
          name: e.name.trim(),
          note: e.note.trim() || null,
          isActive: e.isActive,
          roleIds: e.roleIds,
          memberIds: e.memberIds,
        };
        await (e.id === null ? this.access.createGroup(input) : this.access.updateGroup(e.id, input));
      } else {
        await this.access.setUserRoles(e.id, e.roleIds);
      }

      this.entwurf.set(null);
      await this.laden();
    } catch (error) {
      this.problems.set(meldungen(error));
    } finally {
      this.busy.set(false);
    }
  }

  protected async loeschen(): Promise<void> {
    const e = this.entwurf();
    if (!e || e.art === 'person' || e.id === null) {
      return;
    }

    this.busy.set(true);
    try {
      await (e.art === 'rolle' ? this.access.deleteRole(e.id) : this.access.deleteGroup(e.id));
      this.entwurf.set(null);
      await this.laden();
    } catch (error) {
      this.problems.set(meldungen(error));
    } finally {
      this.busy.set(false);
    }
  }
}

/**
 * Was der Server zu sagen hatte. Anders als bei den Formularen anderswo
 * kommen hier auch Konflikte zurück -- „gibt es schon", „bleibt" --, und die
 * stehen im `detail`, nicht in `errors`.
 */
function meldungen(error: unknown): string[] {
  const body = (error as { error?: { errors?: Record<string, string[]>; detail?: string } })?.error;
  const felder = body?.errors ? Object.values(body.errors).flat() : [];
  if (felder.length > 0) {
    return felder;
  }
  return [body?.detail ?? 'Das hat nicht geklappt.'];
}
