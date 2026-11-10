import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';

export interface DemoHouse {
  key: string;
  name: string;
  isCurrent: boolean;
}

/**
 * Die Häuser, zwischen denen sich in der Entwicklung wechseln lässt.
 *
 * Im Betrieb gibt es den Pfad nicht; die Liste bleibt leer, und die
 * Oberfläche zeigt nur die Kennzeichnung. Eine Antwort, die keine Liste ist,
 * zählt ebenso als leer -- ein unbekannter /api-Pfad liefert die index.html
 * mit 200, und daraus soll kein Menü werden.
 */
@Injectable({ providedIn: 'root' })
export class DemoHousesService {
  private readonly http = inject(HttpClient);

  private readonly loaded = signal<DemoHouse[]>([]);

  readonly houses = this.loaded.asReadonly();

  async load(): Promise<void> {
    try {
      const antwort = await firstValueFrom(this.http.get<unknown>('/api/development/houses'));
      this.loaded.set(Array.isArray(antwort) ? (antwort as DemoHouse[]) : []);
    } catch {
      this.loaded.set([]);
    }
  }

  /** Tauscht den Bestand. Was danach auf dem Bildschirm steht, ist alt -- die Seite muss neu laden. */
  async switchTo(key: string): Promise<void> {
    await firstValueFrom(
      this.http.post(`/api/development/houses/${encodeURIComponent(key)}`, null),
    );
  }
}
