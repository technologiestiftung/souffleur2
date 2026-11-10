import { Component, ElementRef, afterNextRender, computed, inject, input, model, output, viewChild } from '@angular/core';
import { DomSanitizer, SafeResourceUrl } from '@angular/platform-browser';
import { DocumentsService, SouffleurDocument, dateiart, zeigbar } from '../../core/documents.service';
import { IconComponent } from '../../shared/icon.component';

/**
 * Ein Dokument groß ansehen, ohne es herunterzuladen -- wie in einer
 * Lightbox: dunkler Grund, das Dokument in der Mitte, blättern mit den
 * Pfeiltasten durch das, was die Liste gerade zeigt.
 *
 * Was der Browser nicht selbst darstellen kann (Word, Excel, TIFF), bekommt
 * einen Hinweis und den Knopf zum Herunterladen. Eine Umwandlung am Server
 * gibt es dafür bewusst nicht -- sie bräuchte LibreOffice im Image.
 */
@Component({
  selector: 'souffleur-vorschau',
  imports: [IconComponent],
  host: {
    '(document:keydown.escape)': 'closed.emit()',
    '(document:keydown.arrowleft)': 'blaettern(-1)',
    '(document:keydown.arrowright)': 'blaettern(1)',
  },
  template: `
    <div
      #rahmen
      role="dialog"
      aria-modal="true"
      [attr.aria-label]="'Vorschau: ' + doc().name"
      tabindex="-1"
      class="fixed inset-0 z-50 flex flex-col bg-black/85 outline-none"
      (click)="daneben($event)"
    >
      <header class="flex items-center gap-3 px-4 py-3 text-white">
        <div class="min-w-0 flex-1">
          <div class="truncate text-sm font-semibold">{{ doc().name }}</div>
          <div class="text-xs text-white/60">
            {{ position() + 1 }} von {{ docs().length }}
            @if (doc().folderPath) {
              · <span class="font-mono">{{ doc().folderPath }}</span>
            }
          </div>
        </div>
        @if (darfAendern()) {
          <button
            type="button"
            (click)="aendern.emit(doc())"
            class="cursor-pointer rounded-md border border-white/30 bg-transparent px-3 py-1.5 text-sm text-white hover:bg-white/10"
          >
            ändern
          </button>
        }
        <a
          [href]="downloadUrl()"
          class="flex items-center gap-1.5 rounded-md border border-white/30 px-3 py-1.5 text-sm text-white no-underline hover:bg-white/10"
        >
          <souffleur-icon name="download" [size]="14" />
          herunterladen
        </a>
        <button
          type="button"
          (click)="closed.emit()"
          aria-label="Vorschau schließen"
          class="grid size-8 cursor-pointer place-items-center rounded-md border-0 bg-transparent text-xl text-white hover:bg-white/10"
        >
          ✕
        </button>
      </header>

      <div class="relative flex min-h-0 flex-1 items-center justify-center px-14 pb-6" data-buehne>
        @if (docs().length > 1) {
          <button
            type="button"
            (click)="blaettern(-1)"
            aria-label="Voriges Dokument"
            class="absolute left-3 top-1/2 grid size-10 -translate-y-1/2 cursor-pointer place-items-center rounded-full border-0 bg-white/10 text-2xl text-white hover:bg-white/20"
          >
            ‹
          </button>
          <button
            type="button"
            (click)="blaettern(1)"
            aria-label="Nächstes Dokument"
            class="absolute right-3 top-1/2 grid size-10 -translate-y-1/2 cursor-pointer place-items-center rounded-full border-0 bg-white/10 text-2xl text-white hover:bg-white/20"
          >
            ›
          </button>
        }

        @if (doc().isMissing) {
          <p class="m-0 max-w-md text-center text-sm text-white/80">
            Die Datei fehlt im Ordner — verschoben oder gelöscht? Das nächste Einlesen findet sie wieder, wenn sie noch da ist.
          </p>
        } @else if (!kannZeigen()) {
          <div class="flex max-w-md flex-col items-center gap-3 text-center text-white/80">
            <p class="m-0 text-sm">
              Für {{ doc().originalName }} gibt es keine Vorschau im Browser — herunterladen und im passenden Programm öffnen.
            </p>
            <a
              [href]="downloadUrl()"
              class="rounded-md bg-white px-4 py-2 text-sm font-semibold text-black no-underline"
            >
              Herunterladen
            </a>
          </div>
        } @else {
          @switch (art()) {
            @case ('bild') {
              <img [src]="url()" [alt]="doc().name" class="max-h-full max-w-full rounded object-contain shadow-2xl" />
            }
            @case ('pdf') {
              <iframe [src]="rahmenUrl()" [title]="doc().name" class="size-full rounded bg-white shadow-2xl"></iframe>
            }
            @case ('video') {
              <video [src]="url()" controls class="max-h-full max-w-full rounded shadow-2xl"></video>
            }
            @case ('audio') {
              <div class="w-full max-w-xl rounded-lg bg-white/10 p-6 text-center text-white">
                <div class="mb-4 text-sm">{{ doc().name }}</div>
                <audio [src]="url()" controls class="w-full"></audio>
              </div>
            }
            @default {
              <iframe [src]="rahmenUrl()" [title]="doc().name" class="size-full max-w-4xl rounded bg-white shadow-2xl"></iframe>
            }
          }
        }
      </div>
    </div>
  `,
})
export class VorschauComponent {
  private readonly documents = inject(DocumentsService);
  private readonly rahmen = viewChild.required<ElementRef<HTMLElement>>('rahmen');

  readonly docs = input.required<readonly SouffleurDocument[]>();
  readonly position = model.required<number>();
  readonly darfAendern = input(false);

  readonly closed = output<void>();
  readonly aendern = output<SouffleurDocument>();

  protected readonly doc = computed(() => this.docs()[this.position()] ?? this.docs()[0]);
  protected readonly art = computed(() => dateiart(this.doc()));
  protected readonly kannZeigen = computed(() => zeigbar(this.doc()));

  protected readonly url = computed(() => this.documents.previewUrl(this.doc().id));

  // Ein iframe nimmt nur ausdrücklich freigegebene Adressen. Diese ist ein
  // fester Pfad der eigenen API mit einer Kennung -- nichts, was jemand
  // von außen hineinschreiben könnte.
  private readonly sanitizer = inject(DomSanitizer);
  protected readonly rahmenUrl = computed<SafeResourceUrl>(() =>
    this.sanitizer.bypassSecurityTrustResourceUrl(this.url()),
  );
  protected readonly downloadUrl = computed(() => this.documents.fileUrl(this.doc().id));

  constructor() {
    // Den Fokus hereinholen, damit Escape und Pfeiltasten sofort greifen und
    // Vorleseprogramme wissen, wo sie sind.
    afterNextRender(() => this.rahmen().nativeElement.focus());
  }

  protected blaettern(schritt: number): void {
    const anzahl = this.docs().length;
    if (anzahl > 1) {
      this.position.set((this.position() + schritt + anzahl) % anzahl);
    }
  }

  /** Ein Klick auf den dunklen Grund schließt -- nicht einer auf das Dokument. */
  protected daneben(event: MouseEvent): void {
    const ziel = event.target as HTMLElement;
    if (ziel === event.currentTarget || ziel.hasAttribute('data-buehne')) {
      this.closed.emit();
    }
  }
}
