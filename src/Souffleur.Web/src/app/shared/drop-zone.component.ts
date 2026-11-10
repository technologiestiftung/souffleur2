import { Component, ElementRef, input, model, output, signal, viewChild } from '@angular/core';

/**
 * Die Fläche, auf die man eine Datei fallen lässt.
 *
 * Ziehen ist der kürzere Weg: Der Beleg liegt im Mailanhang oder auf dem
 * Schreibtisch, und von dort geht er in einer Bewegung hierher. Der Weg über
 * die Dateiauswahl bleibt daneben bestehen -- ein Klick auf die Fläche
 * öffnet sie.
 */
@Component({
  selector: 'souffleur-drop-zone',
  templateUrl: './drop-zone.component.html',
})
export class DropZoneComponent {
  /** Was der Dateiauswahl vorgeschlagen und beim Fallenlassen geprüft wird. */
  readonly accept = input('');

  /** Null heißt: keine Grenze. Sonst die des Servers, damit die Absage hier fällt und nicht dort. */
  readonly maxBytes = input<number | null>(null);

  readonly label = input('Datei hierher ziehen');

  /** Wie die Dateiauswahl dahinter heißt -- sie trägt den Namen für Vorleseprogramme. */
  readonly inputLabel = input('Datei wählen');
  readonly hint = input('');

  readonly file = model<File | null>(null);

  /**
   * Mehrere auf einmal: Dann bleibt die Fläche leer und meldet über
   * `filesChange`, was angenommen ist -- die Seite kümmert sich um die Liste.
   * Die Belege bleiben bei einer Datei; ein Beleg ist eine Rechnung.
   */
  readonly multiple = input(false);
  readonly filesChange = output<File[]>();

  private readonly picker = viewChild.required<ElementRef<HTMLInputElement>>('picker');

  protected readonly over = signal(false);
  protected readonly problem = signal('');

  /**
   * Öffnet die Dateiauswahl. Auch von außen, damit eine Seite, die mit
   * „hochladen" betreten wird, gleich fragt, welche Datei. Der Browser
   * lässt das nur kurz nach einem Klick zu -- kommt der Aufruf zu spät,
   * passiert nichts, und die Fläche steht ja da.
   */
  browse(): void {
    this.picker().nativeElement.click();
  }

  protected onPicked(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.takeAll(input.files);

    // Zurücksetzen, sonst meldet dieselbe Datei beim zweiten Mal keine Änderung.
    input.value = '';
  }

  protected onDragOver(event: DragEvent): void {
    event.preventDefault();
    this.over.set(true);
  }

  protected onDragLeave(): void {
    this.over.set(false);
  }

  protected onDrop(event: DragEvent): void {
    event.preventDefault();
    this.over.set(false);
    this.takeAll(event.dataTransfer?.files ?? null);
  }

  private takeAll(files: FileList | null): void {
    if (!files || files.length === 0) {
      return;
    }
    if (!this.multiple()) {
      this.take(files[0]);
      return;
    }

    // Was nicht passt, wird einzeln genannt; der Rest geht trotzdem durch.
    const angenommen: File[] = [];
    const abgelehnt: string[] = [];
    for (const file of Array.from(files)) {
      const grund = this.reject(file);
      if (grund) {
        abgelehnt.push(grund);
      } else {
        angenommen.push(file);
      }
    }
    this.problem.set(abgelehnt.join(' '));
    if (angenommen.length > 0) {
      this.filesChange.emit(angenommen);
    }
  }

  protected clear(): void {
    this.problem.set('');
    this.file.set(null);
  }

  protected size(file: File): string {
    return file.size < 1024 * 1024
      ? `${Math.round(file.size / 1024)} kB`
      : `${(file.size / 1024 / 1024).toFixed(1)} MB`;
  }

  /**
   * Annehmen oder begründet ablehnen. Der Server prüft dasselbe noch einmal --
   * hier geht es darum, dass niemand erst nach dem Hochladen erfährt, dass
   * eine Datei nicht als Beleg taugt.
   */
  private take(file: File | null): void {
    if (!file) {
      return;
    }

    const grund = this.reject(file);
    if (grund) {
      this.problem.set(grund);
      return;
    }

    this.problem.set('');
    this.file.set(file);
  }

  /** Warum eine Datei nicht passt -- oder nichts, wenn sie passt. */
  private reject(file: File): string {
    const limit = this.maxBytes();
    if (limit && file.size > limit) {
      return `${file.name} ist ${this.size(file)} groß — mehr als ${Math.round(limit / 1024 / 1024)} MB gehen nicht.`;
    }
    if (!this.fits(file)) {
      return `${file.name} ist hier nicht vorgesehen. ${this.hint()}`.trim();
    }
    return '';
  }

  /**
   * Passt die Datei zur Vorgabe? Geprüft wird gegen Endung und Medientyp:
   * Ein Foto vom iPhone kommt oft ohne Typ an, dann trägt nur die Endung.
   */
  private fits(file: File): boolean {
    const allowed = this.accept()
      .split(',')
      .map((entry) => entry.trim().toLowerCase())
      .filter(Boolean);

    if (allowed.length === 0) {
      return true;
    }

    const name = file.name.toLowerCase();
    const type = file.type.toLowerCase();

    return allowed.some((entry) => {
      if (entry.startsWith('.')) {
        return name.endsWith(entry);
      }

      if (entry.endsWith('/*')) {
        return type.startsWith(entry.slice(0, -1));
      }

      return type === entry;
    });
  }
}
