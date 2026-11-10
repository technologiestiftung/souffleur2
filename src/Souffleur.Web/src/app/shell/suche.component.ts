import {
  Component,
  DestroyRef,
  ElementRef,
  computed,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router } from '@angular/router';
import { Subject } from 'rxjs';
import { debounceTime, distinctUntilChanged, switchMap, tap } from 'rxjs/operators';
import { Bereich, MINDESTLAENGE, SearchService, Treffer } from '../core/search.service';
import { AnsageService } from '../shared/ansage.service';
import { IconComponent } from '../shared/icon.component';

/** Ein Fund mit seinem Bereich -- so, wie die Tastatur die Liste durchläuft. */
interface Zeile {
  bereich: Bereich;
  treffer: Treffer;
  index: number;
}

/**
 * Das Suchfeld in der Kopfzeile: findet beim Tippen über alle Module, zeigt
 * die Funde nach Bereichen und springt beim Klick dorthin.
 *
 * Ein Feld statt eines je Modul, weil die Frage „wo war das?" selten weiß,
 * in welchem Modul die Antwort liegt. Die Bereiche in der Liste geben den
 * Zusammenhang, den die Frage nicht hatte.
 *
 * Tastatur: Strg/Cmd+K oder „/" springt ins Feld, Pfeile wandern durch die
 * Funde, Enter öffnet, Escape schließt. Die Liste ist eine Listbox am
 * Suchfeld (aria-controls, aria-activedescendant), damit ein Vorleser weiß,
 * was er vor sich hat. Das Feld bleibt eine Searchbox und keine Combobox:
 * Die Rolle teilen sich sonst alle Auswahllisten der Seite, und wer „die
 * erste Auswahlliste" sucht, bekäme das Suchfeld.
 */
@Component({
  selector: 'souffleur-suche',
  imports: [IconComponent],
  templateUrl: './suche.component.html',
  host: {
    class: 'relative flex shrink-0 items-center px-2',
    '(document:click)': 'aussen($event)',
    '(document:keydown)': 'tastenkuerzel($event)',
  },
})
export class SucheComponent {
  private readonly search = inject(SearchService);
  private readonly router = inject(Router);
  private readonly ansage = inject(AnsageService);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly destroyRef = inject(DestroyRef);

  private readonly feld = viewChild.required<ElementRef<HTMLInputElement>>('feld');
  private readonly eingaben = new Subject<string>();

  protected readonly mindestlaenge = MINDESTLAENGE;
  protected readonly text = signal('');
  protected readonly offen = signal(false);
  protected readonly laedt = signal(false);
  protected readonly bereiche = signal<Bereich[]>([]);

  /** Welche Zeile die Tastatur gerade hält. */
  protected readonly aktiv = signal(0);

  /** Alle Funde hintereinander, damit Pfeiltasten über Bereichsgrenzen laufen. */
  protected readonly zeilen = computed<Zeile[]>(() => {
    const zeilen: Zeile[] = [];
    for (const bereich of this.bereiche()) {
      for (const treffer of bereich.treffer) {
        zeilen.push({ bereich, treffer, index: zeilen.length });
      }
    }
    return zeilen;
  });

  /** Nur mit genug Text gibt es etwas zu zeigen -- auch „nichts gefunden" erst dann. */
  protected readonly zeigtListe = computed(
    () => this.offen() && this.text().trim().length >= MINDESTLAENGE,
  );

  constructor() {
    // Kurz warten, dann die letzte Eingabe schicken; eine neue Eingabe bricht
    // die laufende Anfrage ab (switchMap) -- so kann keine späte Antwort ein
    // frischeres Ergebnis überschreiben.
    this.eingaben
      .pipe(
        debounceTime(150),
        distinctUntilChanged(),
        tap(() => this.laedt.set(true)),
        switchMap((text) => this.search.suchen(text)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((bereiche) => {
        this.laedt.set(false);
        this.bereiche.set(bereiche);
        this.aktiv.set(0);
        this.ansagen(bereiche);
      });
  }

  protected tippen(wert: string): void {
    this.text.set(wert);
    this.offen.set(true);
    if (wert.trim().length < MINDESTLAENGE) {
      this.bereiche.set([]);
      this.laedt.set(false);
    }
    this.eingaben.next(wert);
  }

  protected fokus(): void {
    if (this.text().trim().length >= MINDESTLAENGE) {
      this.offen.set(true);
    }
  }

  protected tasteImFeld(event: KeyboardEvent): void {
    const anzahl = this.zeilen().length;

    switch (event.key) {
      case 'ArrowDown':
        event.preventDefault();
        if (anzahl > 0) {
          this.offen.set(true);
          this.aktiv.update((i) => (i + 1) % anzahl);
        }
        break;

      case 'ArrowUp':
        event.preventDefault();
        if (anzahl > 0) {
          this.aktiv.update((i) => (i - 1 + anzahl) % anzahl);
        }
        break;

      case 'Enter': {
        const zeile = this.zeilen()[this.aktiv()];
        if (this.zeigtListe() && zeile) {
          event.preventDefault();
          void this.oeffnen(zeile.treffer);
        }
        break;
      }

      case 'Escape':
        event.preventDefault();
        if (this.offen()) {
          this.offen.set(false);
        } else {
          this.leeren();
        }
        break;
    }
  }

  /**
   * Strg/Cmd+K von überall; „/" nur, wenn der Fokus nicht ohnehin in einem
   * Eingabefeld steht -- dort gehört das Zeichen zum Text.
   */
  protected tastenkuerzel(event: KeyboardEvent): void {
    const inFeld = event.target === this.feld().nativeElement;
    const inEingabe =
      event.target instanceof HTMLElement &&
      (event.target.matches('input, textarea, select, [contenteditable]') || inFeld);

    if ((event.key === 'k' || event.key === 'K') && (event.metaKey || event.ctrlKey)) {
      event.preventDefault();
      this.feld().nativeElement.focus();
      this.feld().nativeElement.select();
      return;
    }

    if (event.key === '/' && !inEingabe && !event.metaKey && !event.ctrlKey && !event.altKey) {
      event.preventDefault();
      this.feld().nativeElement.focus();
    }
  }

  protected aussen(event: MouseEvent): void {
    if (this.offen() && !this.host.nativeElement.contains(event.target as Node)) {
      this.offen.set(false);
    }
  }

  protected async oeffnen(treffer: Treffer): Promise<void> {
    this.offen.set(false);
    this.feld().nativeElement.blur();
    await this.router.navigateByUrl(treffer.link);
  }

  protected leeren(): void {
    this.text.set('');
    this.bereiche.set([]);
    this.offen.set(false);
    this.eingaben.next('');
  }

  protected zeilenVon(bereich: Bereich): Zeile[] {
    return this.zeilen().filter((z) => z.bereich === bereich);
  }

  protected zeilenId(index: number): string {
    return `suche-treffer-${index}`;
  }

  /** Zeile unter der Maus wird die aktive -- Pfeiltasten setzen dort fort. */
  protected anfahren(index: number): void {
    this.aktiv.set(index);
  }

  private ansagen(bereiche: Bereich[]): void {
    const anzahl = bereiche.reduce((summe, b) => summe + b.treffer.length, 0);
    if (anzahl === 0) {
      this.ansage.sage('Nichts gefunden.');
      return;
    }

    const wo = bereiche.length === 1 ? bereiche[0].label : `${bereiche.length} Bereichen`;
    this.ansage.sage(`${anzahl} Treffer in ${wo}. Mit den Pfeiltasten auswählen.`);
  }
}
