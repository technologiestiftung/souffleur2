import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { CatalogEntry, CatalogsService } from '../../core/catalogs.service';
import { DutiesService, DutyTemplate } from '../../core/duties.service';
import { ActionsRegistry } from '../../shell/actions.registry';
import { einstellungsBereiche } from './bereiche';
import { AuthService } from '../../core/auth.service';
import { DialogComponent } from '../../shared/dialog.component';
import { messagesFrom } from '../calendar/event-dialog.component';
import { MeldungComponent } from '../../shared/meldung.component';
import { GroupSummary, GroupsService } from '../../core/groups.service';

/** Eine Zeile im Bogen: Funktion und Anzahl. */
interface DraftLine {
  functionId: string;
  count: number;

  /** Aus welcher Gruppe vorgeschlagen wird; leer heißt: aus allen. */
  groupId: string;
}

/**
 * Wer bei welcher Terminart gebraucht wird.
 *
 * Der Bedarf hängt an der Terminkategorie und nicht an einer eigenen
 * Schichtliste: Dienste entstehen im Theater an Vorstellungen und Proben, und
 * die stehen schon im Kalender. Eine zweite Zeitachse daneben wäre eine zweite
 * Wahrheit darüber, was an einem Abend stattfindet.
 */
@Component({
  selector: 'souffleur-dienstbedarf',
  imports: [FormsModule, RouterLink, DialogComponent, MeldungComponent],
  templateUrl: './dienstbedarf.component.html',
})
export class DienstbedarfComponent implements OnInit {
  private readonly duties = inject(DutiesService);
  private readonly catalogs = inject(CatalogsService);
  private readonly actions = inject(ActionsRegistry);
  private readonly auth = inject(AuthService);
  private readonly groupList = inject(GroupsService);

  protected readonly templates = signal<DutyTemplate[]>([]);
  protected readonly categories = signal<CatalogEntry[]>([]);
  protected readonly functions = signal<CatalogEntry[]>([]);
  protected readonly groups = signal<GroupSummary[]>([]);

  /** Ob der Hinweis auf „Zugänge → Gruppen" eine Tür ist, die aufgeht. */
  protected readonly darfGruppen = computed(() => this.auth.can('AccountsManage'));

  protected readonly loading = signal(true);
  protected readonly busy = signal(false);
  protected readonly problems = signal<string[]>([]);

  /** Für welche Terminart gerade ein Bedarf entsteht -- null heißt: kein Fenster. */
  protected readonly editing = signal<string | null>(null);
  protected readonly lines = signal<DraftLine[]>([]);
  protected readonly note = signal('');

  protected readonly heading = computed(() => {
    const category = this.categories().find((entry) => entry.id === this.editing());
    return category ? `Bedarf für ${category.label}` : 'Bedarf';
  });

  /** Terminarten ohne Bedarf -- für die lohnt der Knopf. */
  protected readonly without = computed(() => {
    const taken = new Set(this.templates().map((template) => template.categoryId));
    return this.categories().filter((entry) => !taken.has(entry.id));
  });

  async ngOnInit(): Promise<void> {
    this.actions.set(
      'Einstellungen',
      [],
      [
        ...einstellungsBereiche(this.auth.can('AccountsManage')),
        { label: 'Terminkategorien', icon: 'etikett', path: '/einstellungen/listen/EventCategory' },
        {
          label: 'Funktionen am Termin',
          icon: 'funktion',
          path: '/einstellungen/listen/ParticipationRole',
        },
      ],
    );

    const [categories, functions] = await Promise.all([
      this.catalogs.entries('EventCategory'),
      this.catalogs.entries('ParticipationRole'),
    ]);

    this.categories.set(categories);
    this.functions.set(functions);
    this.groups.set(await this.groupList.active());

    await this.reload();
    this.loading.set(false);
  }

  protected async reload(): Promise<void> {
    this.problems.set([]);

    try {
      this.templates.set(await this.duties.templates());
    } catch (error) {
      this.problems.set(messagesFrom(error));
    }
  }

  protected startNew(categoryId: string): void {
    if (!categoryId) {
      return;
    }

    this.problems.set([]);
    this.editing.set(categoryId);
    this.note.set('');
    this.lines.set([{ functionId: '', count: 1, groupId: '' }]);
  }

  protected startEdit(template: DutyTemplate): void {
    this.problems.set([]);
    this.editing.set(template.categoryId);
    this.note.set(template.note ?? '');
    this.lines.set(
      template.requirements.map((requirement) => ({
        functionId: requirement.functionId,
        count: requirement.count,
        groupId: requirement.groupId ?? '',
      })),
    );
  }

  protected cancel(): void {
    this.editing.set(null);
    this.lines.set([]);
    this.note.set('');
  }

  protected addLine(): void {
    this.lines.update((current) => [...current, { functionId: '', count: 1, groupId: '' }]);
  }

  protected removeLine(index: number): void {
    this.lines.update((current) => current.filter((_, position) => position !== index));
  }

  protected patchLine(index: number, change: Partial<DraftLine>): void {
    this.lines.update((current) =>
      current.map((line, position) => (position === index ? { ...line, ...change } : line)),
    );
  }

  protected async save(): Promise<void> {
    const categoryId = this.editing();
    const requirements = this.lines()
      .filter((line) => line.functionId)
      .map((line) => ({
        functionId: line.functionId,
        count: Number(line.count) || 1,
        groupId: line.groupId || null,
      }));

    if (!categoryId) {
      return;
    }

    if (!requirements.length) {
      this.problems.set(['Ein Bedarf ohne Funktionen sagt nichts aus.']);
      return;
    }

    this.busy.set(true);
    this.problems.set([]);

    try {
      await this.duties.save(categoryId, requirements, this.note().trim() || null);
      this.cancel();
      await this.reload();
    } catch (error) {
      this.problems.set(messagesFrom(error));
    } finally {
      this.busy.set(false);
    }
  }

  protected async remove(template: DutyTemplate): Promise<void> {
    this.busy.set(true);
    this.problems.set([]);

    try {
      await this.duties.remove(template.categoryId);
      await this.reload();
    } catch (error) {
      this.problems.set(messagesFrom(error));
    } finally {
      this.busy.set(false);
    }
  }

  protected functionLabel(id: string): string {
    return this.functions().find((entry) => entry.id === id)?.label ?? '—';
  }
}
