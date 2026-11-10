import { Component, ElementRef, OnDestroy, OnInit, computed, inject, input, signal } from '@angular/core';
import { DocumentsService, SouffleurDocument, dateiart, endung } from '../../core/documents.service';
import { pdfVorschau } from '../../shared/pdf-vorschau';

/**
 * Das kleine Bild vor einem Dokument.
 *
 * Bilder zeigen sich selbst, PDFs ihre erste Seite, alles andere ein Kärtchen
 * mit der Endung -- so erkennt man „das Programmheft" am Aussehen, nicht am
 * Dateinamen. PDFs werden erst gerechnet, wenn die Zeile ins Bild kommt.
 */
@Component({
  selector: 'souffleur-vorschaubild',
  host: { class: 'block shrink-0' },
  template: `
    <div
      class="grid size-12 place-items-center overflow-hidden rounded-md border border-border bg-surface-sunken"
    >
      @if (bild(); as src) {
        <img
          [src]="src"
          alt=""
          loading="lazy"
          decoding="async"
          (error)="kaputt.set(true)"
          class="size-full object-cover"
          [class.object-top]="art() === 'pdf'"
        />
      } @else {
        <span class="text-[10px] font-semibold uppercase tracking-wide" [class]="farbe()">
          {{ kuerzel() }}
        </span>
      }
    </div>
  `,
})
export class VorschaubildComponent implements OnInit, OnDestroy {
  private readonly documents = inject(DocumentsService);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  readonly doc = input.required<SouffleurDocument>();

  protected readonly art = computed(() => dateiart(this.doc()));
  protected readonly kuerzel = computed(() => endung(this.doc()) || 'Datei');
  protected readonly farbe = computed(() => {
    switch (this.art()) {
      case 'pdf':
        return 'text-critical';
      case 'office':
        return 'text-brand';
      default:
        return 'text-content-muted';
    }
  });

  protected readonly kaputt = signal(false);
  private readonly pdfBild = signal<string | null>(null);
  private beobachter?: IntersectionObserver;

  protected readonly bild = computed(() => {
    if (this.kaputt() || this.doc().isMissing) {
      return null;
    }
    const e = endung(this.doc());
    if (this.art() === 'bild' && e !== 'tif' && e !== 'tiff') {
      return this.documents.previewUrl(this.doc().id);
    }
    return this.art() === 'pdf' ? this.pdfBild() : null;
  });

  ngOnInit(): void {
    if (this.art() !== 'pdf' || this.doc().isMissing) {
      return;
    }
    this.beobachter = new IntersectionObserver(
      (eintraege) => {
        if (eintraege.some((e) => e.isIntersecting)) {
          this.beobachter?.disconnect();
          void pdfVorschau(this.documents.previewUrl(this.doc().id), 48).then((url) =>
            url ? this.pdfBild.set(url) : this.kaputt.set(true),
          );
        }
      },
      { rootMargin: '200px' },
    );
    this.beobachter.observe(this.host.nativeElement);
  }

  ngOnDestroy(): void {
    this.beobachter?.disconnect();
  }
}
