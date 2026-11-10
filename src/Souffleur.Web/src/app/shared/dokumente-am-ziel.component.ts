import { Component, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import {
  DocumentTarget,
  DocumentTargetKey,
  DocumentsService,
  SouffleurDocument,
} from '../core/documents.service';
import { IconComponent } from './icon.component';

/**
 * Die Dokumente an einem Gegenstand, der schon gespeichert ist: ansehen,
 * Vorhandenes aus der Ablage anheften, Neues hochladen, lösen.
 *
 * Für einen Gegenstand im Anlegen gibt es das hier nicht -- dort braucht es
 * Vormerken bis zum Speichern, und das gehört dem jeweiligen Bogen (Aufgaben).
 * Lösen nimmt nur die Verknüpfung weg; das Dokument bleibt in der Ablage.
 */
@Component({
  selector: 'souffleur-dokumente-am-ziel',
  imports: [FormsModule, IconComponent],
  template: `
    <div class="flex items-baseline justify-between gap-2">
      <span class="text-xs font-medium text-content-muted">Dokumente</span>
      @if (linked().length > 0) {
        <span class="text-[11px] text-content-faint tabular">{{ linked().length }}</span>
      }
    </div>

    @if (linked().length > 0) {
      <ul class="m-0 mt-1 list-none divide-y divide-border p-0" aria-label="Dokumente">
        @for (doc of linked(); track doc.id) {
          <li class="flex items-center gap-2 py-1 text-sm">
            <souffleur-icon name="dokumente" [size]="12" class="shrink-0 text-content-faint" />
            <a
              [href]="fileUrl(doc)"
              target="_blank"
              rel="noopener"
              class="min-w-0 flex-1 truncate text-brand underline-offset-2 hover:underline"
            >
              {{ doc.name }}
            </a>
            <button
              type="button"
              (click)="detach(doc)"
              [attr.aria-label]="doc.name + ' lösen'"
              title="Lösen — das Dokument bleibt in der Ablage"
              class="shrink-0 cursor-pointer border-0 bg-transparent px-1 text-sm text-content-faint hover:text-critical"
            >
              ✕
            </button>
          </li>
        }
      </ul>
    }

    @if (problem()) {
      <p class="m-0 mt-1 text-xs text-critical">{{ problem() }}</p>
    }

    <div class="mt-1.5 flex flex-wrap items-center gap-1.5">
      <select
        [ngModel]="chosen()"
        (ngModelChange)="chosen.set($event)"
        aria-label="Vorhandenes Dokument"
        class="min-w-0 flex-1 basis-56 rounded-md border border-border bg-surface px-2 py-1 text-sm text-content"
      >
        <option value="">— vorhandenes Dokument —</option>
        @for (doc of choices(); track doc.id) {
          <option [value]="doc.id">{{ doc.name }}</option>
        }
      </select>
      <button
        type="button"
        (click)="attach()"
        [disabled]="!chosen() || busy()"
        class="cursor-pointer rounded-md border border-border bg-transparent px-2.5 py-1 text-sm text-content disabled:opacity-50"
      >
        Anheften
      </button>
      <button
        type="button"
        (click)="datei.click()"
        [disabled]="busy()"
        class="cursor-pointer rounded-md border border-border bg-transparent px-2.5 py-1 text-sm text-content disabled:opacity-50"
      >
        {{ busy() ? 'Wird hochgeladen …' : 'Datei hochladen …' }}
      </button>
      <input
        #datei
        type="file"
        multiple
        class="hidden"
        aria-label="Datei wählen"
        (change)="upload($event)"
      />
    </div>
  `,
})
export class DokumenteAmZielComponent {
  private readonly documents = inject(DocumentsService);

  readonly target = input.required<DocumentTargetKey>();
  readonly targetId = input.required<string>();

  protected readonly linked = signal<SouffleurDocument[]>([]);
  private readonly all = signal<SouffleurDocument[]>([]);
  protected readonly chosen = signal('');
  protected readonly busy = signal(false);
  protected readonly problem = signal('');

  /** Was noch nicht hängt -- nur das steht zur Wahl. */
  protected readonly choices = computed(() => {
    const taken = new Set(this.linked().map((d) => d.id));
    return this.all().filter((d) => !taken.has(d.id));
  });

  constructor() {
    effect(() => {
      const id = this.targetId();
      untracked(() => void this.load(id));
    });
  }

  private async load(id: string): Promise<void> {
    const [linked, all] = await Promise.all([
      this.documents.list({ target: DocumentTarget[this.target()], targetId: id }).catch(() => []),
      this.documents.list().catch(() => []),
    ]);
    // Zwischendurch ein anderer Gegenstand? Dann gehört das nicht hierher.
    if (this.targetId() === id) {
      this.linked.set(linked);
      this.all.set(all);
    }
  }

  protected async attach(): Promise<void> {
    const id = this.chosen();
    if (!id) {
      return;
    }
    this.chosen.set('');
    await this.run(() => this.documents.link(id, DocumentTarget[this.target()], this.targetId()));
  }

  protected async detach(doc: SouffleurDocument): Promise<void> {
    const link = doc.links.find(
      (l) => l.targetKey === this.target() && l.targetId === this.targetId(),
    );
    if (link) {
      await this.run(() => this.documents.unlink(doc.id, link.id));
    }
  }

  protected async upload(event: Event): Promise<void> {
    const field = event.target as HTMLInputElement;
    const files = Array.from(field.files ?? []);
    field.value = '';
    await this.run(async () => {
      for (const file of files) {
        const stored = await this.documents.upload(file, {
          name: null,
          note: null,
          tags: [],
          restricted: false,
          groupIds: [],
        });
        await this.documents.link(stored.id, DocumentTarget[this.target()], this.targetId());
      }
    });
  }

  protected fileUrl(doc: SouffleurDocument): string {
    return this.documents.fileUrl(doc.id);
  }

  private async run(step: () => Promise<unknown>): Promise<void> {
    this.busy.set(true);
    this.problem.set('');
    try {
      await step();
      await this.load(this.targetId());
    } catch {
      this.problem.set('Das hat nicht geklappt.');
    } finally {
      this.busy.set(false);
    }
  }
}
