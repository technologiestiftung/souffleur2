import {
  Component,
  computed,
  effect,
  inject,
  input,
  model,
  signal,
  untracked,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import {
  Bookmark,
  BookmarkDraft,
  BookmarkTarget,
  BookmarksService,
} from '../core/bookmarks.service';

/**
 * Lesezeichen an einem Gegenstand: eine Liste von Adressen mit Stichwort,
 * ein Feld zum Hinzufügen. Derselbe Baustein an der Aufgabe, am Kontakt, am
 * Dokument, am Gastspiel -- wer eine Recherche, ein Video oder die Website
 * der Spielstätte festhalten will, findet überall dasselbe.
 *
 * Zwei Betriebsarten, je nachdem, ob der Gegenstand schon gespeichert ist:
 * Mit `targetId` spricht der Baustein direkt mit dem Server. Ohne (ein Bogen
 * im Anlegen) sammelt er in `ausstehend`, und die Seite legt die Lesezeichen
 * nach dem Speichern an. So kann man beim Anlegen schon anheften, ohne dass
 * der Baustein wissen muss, wie der Gegenstand entsteht.
 *
 * Links öffnen immer in einem neuen Fenster: Wer von der Aufgabe aus ein
 * Video aufmacht, will danach noch in der Aufgabe stehen.
 */
@Component({
  selector: 'souffleur-lesezeichen',
  imports: [FormsModule],
  template: `
    <div class="flex items-baseline justify-between gap-2">
      <span class="text-xs font-medium text-content-muted">Lesezeichen</span>
      @if (zeilen().length > 0) {
        <span class="text-[11px] text-content-faint tabular">{{ zeilen().length }}</span>
      }
    </div>

    @if (zeilen().length > 0) {
      <ul class="m-0 mt-1 list-none divide-y divide-border p-0" aria-label="Lesezeichen">
        @for (zeile of zeilen(); track zeile.key) {
          <li class="flex items-center gap-2 py-1 text-sm">
            <svg
              width="12"
              height="12"
              viewBox="0 0 24 24"
              fill="none"
              stroke="currentColor"
              stroke-width="2"
              aria-hidden="true"
              class="shrink-0 text-content-faint"
            >
              <path
                d="M10 13a5 5 0 0 0 7 0l3-3a5 5 0 0 0-7-7l-1 1M14 11a5 5 0 0 0-7 0l-3 3a5 5 0 0 0 7 7l1-1"
              />
            </svg>
            <a
              [href]="zeile.url"
              target="_blank"
              rel="noopener noreferrer"
              class="min-w-0 flex-1 truncate text-brand underline-offset-2 hover:underline"
              [title]="zeile.url"
              >{{ zeile.label || host(zeile.url) }}</a
            >
            @if (zeile.label) {
              <span class="hidden shrink-0 text-[11px] text-content-faint sm:inline">{{
                host(zeile.url)
              }}</span>
            }
            <button
              type="button"
              (click)="entfernen(zeile)"
              [attr.aria-label]="'Lesezeichen ' + (zeile.label || host(zeile.url)) + ' entfernen'"
              class="shrink-0 cursor-pointer border-0 bg-transparent px-1 text-sm text-content-faint hover:text-critical"
            >
              ✕
            </button>
          </li>
        }
      </ul>
    }

    <div class="mt-1.5 flex flex-wrap items-center gap-1.5">
      <label class="sr-only" [for]="id + '-url'">Adresse</label>
      <input
        [id]="id + '-url'"
        type="url"
        inputmode="url"
        placeholder="https://…"
        [ngModel]="url()"
        (ngModelChange)="url.set($event)"
        (keydown.enter)="$event.preventDefault(); hinzufuegen()"
        class="min-w-0 flex-1 basis-40 rounded-md border border-border bg-surface px-2 py-1 text-sm text-content placeholder:text-content-faint"
      />
      <label class="sr-only" [for]="id + '-label'">Stichwort</label>
      <input
        [id]="id + '-label'"
        type="text"
        placeholder="Stichwort"
        [ngModel]="label()"
        (ngModelChange)="label.set($event)"
        (keydown.enter)="$event.preventDefault(); hinzufuegen()"
        class="min-w-0 flex-1 basis-32 rounded-md border border-border bg-surface px-2 py-1 text-sm text-content placeholder:text-content-faint"
      />
      <button
        type="button"
        (click)="hinzufuegen()"
        [disabled]="!url().trim() || busy()"
        aria-label="Lesezeichen hinzufügen"
        class="cursor-pointer rounded-md border border-border bg-transparent px-2 py-1 text-sm text-content disabled:opacity-50"
      >
        +
      </button>
    </div>
    @if (fehler()) {
      <p role="alert" class="m-0 mt-1 text-xs text-critical">{{ fehler() }}</p>
    }
  `,
  host: { class: 'block' },
})
export class LesezeichenComponent {
  private readonly bookmarks = inject(BookmarksService);

  readonly target = input.required<BookmarkTarget>();

  /** Der Gegenstand -- oder null, solange er noch nicht gespeichert ist. */
  readonly targetId = input<string | null>(null);

  /** Was beim Anlegen gesammelt wurde; die Seite legt es nach dem Speichern an. */
  readonly ausstehend = model<BookmarkDraft[]>([]);

  protected readonly url = signal('');
  protected readonly label = signal('');
  protected readonly busy = signal(false);
  protected readonly fehler = signal('');
  private readonly gespeichert = signal<Bookmark[]>([]);

  /** Eindeutig je Baustein, damit zwei auf einer Seite nicht dieselben Feld-Ids tragen. */
  protected readonly id = `lz-${Math.random().toString(36).slice(2, 8)}`;

  /** Gespeicherte und ausstehende in einer Liste -- für die Anzeige ist es egal, woher sie kommen. */
  protected readonly zeilen = computed(() => {
    if (this.targetId()) {
      return this.gespeichert().map((b) => ({ key: b.id, url: b.url, label: b.label, id: b.id }));
    }
    return this.ausstehend().map((d, i) => ({
      key: `neu-${i}`,
      url: d.url,
      label: d.label,
      id: null,
    }));
  });

  constructor() {
    effect(() => {
      const id = this.targetId();
      const target = this.target();
      untracked(() => void this.laden(target, id));
    });
  }

  private async laden(target: BookmarkTarget, id: string | null): Promise<void> {
    if (!id) {
      this.gespeichert.set([]);
      return;
    }
    try {
      this.gespeichert.set(await this.bookmarks.list(target, id));
    } catch {
      this.gespeichert.set([]);
    }
  }

  protected host(url: string): string {
    return BookmarksService.hostOf(url);
  }

  protected async hinzufuegen(): Promise<void> {
    const roh = this.url().trim();
    if (!roh) {
      return;
    }
    // Wer „youtube.com/…" einfügt, meint die Website -- wie am Server.
    const url = roh.includes('://') ? roh : `https://${roh}`;
    const draft: BookmarkDraft = { url, label: this.label().trim() || null };
    this.fehler.set('');

    const id = this.targetId();
    if (!id) {
      this.ausstehend.update((liste) => [...liste, draft]);
      this.url.set('');
      this.label.set('');
      return;
    }

    this.busy.set(true);
    try {
      const created = await this.bookmarks.add(this.target(), id, draft);
      this.gespeichert.update((liste) => [...liste, created]);
      this.url.set('');
      this.label.set('');
    } catch (e) {
      const errors = (e as { error?: { errors?: Record<string, string[]> } })?.error?.errors;
      this.fehler.set(
        errors ? Object.values(errors).flat().join(' ') : 'Das Lesezeichen wurde nicht angenommen.',
      );
    } finally {
      this.busy.set(false);
    }
  }

  protected async entfernen(zeile: { id: string | null; key: string }): Promise<void> {
    if (!zeile.id) {
      const index = Number(zeile.key.replace('neu-', ''));
      this.ausstehend.update((liste) => liste.filter((_, i) => i !== index));
      return;
    }
    await this.bookmarks.remove(zeile.id);
    this.gespeichert.update((liste) => liste.filter((b) => b.id !== zeile.id));
  }
}
