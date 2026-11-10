import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, of } from 'rxjs';
import { catchError } from 'rxjs/operators';
import { IconName } from '../shared/icon.component';

/** Ein Fund -- und wohin er führt. */
export interface Treffer {
  id: string;
  titel: string;
  untertitel: string | null;
  link: string;
}

/** Die Funde eines Bereichs, in der Reihenfolge der Reiter. */
export interface Bereich {
  key: string;
  label: string;
  icon: IconName;
  treffer: Treffer[];
}

/** Ab so vielen Zeichen fragt die Oberfläche den Server -- derselbe Wert wie dort. */
export const MINDESTLAENGE = 2;

/**
 * Das eine Suchfeld oben fragt hier. Was es zurückbekommt, hat der Server
 * schon nach Rechten und eingeschalteten Modulen gefiltert -- die Oberfläche
 * zeigt, was kommt, und blendet nichts nach.
 */
@Injectable({ providedIn: 'root' })
export class SearchService {
  private readonly http = inject(HttpClient);

  /**
   * Als Observable, nicht als Promise: Wer tippt, überholt die vorige Anfrage,
   * und die muss sich abbrechen lassen -- sonst schreibt eine späte Antwort
   * auf „ham" über das Ergebnis für „hamlet".
   */
  suchen(text: string): Observable<Bereich[]> {
    if (text.trim().length < MINDESTLAENGE) {
      return of([]);
    }

    return this.http
      .get<Bereich[]>('/api/search', { params: { q: text.trim() } })
      .pipe(catchError(() => of([])));
  }
}
