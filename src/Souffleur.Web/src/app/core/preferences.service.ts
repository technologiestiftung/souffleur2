import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';

/**
 * Was jemand für sich eingestellt hat -- bislang nur Reihenfolgen.
 *
 * Am Konto, nicht im Browser: Wer morgens am eigenen Rechner und abends an
 * der Pforte sitzt, soll seine Leiste zweimal gleich vorfinden.
 *
 * Eine Reihenfolge ist eine Liste von Schlüsseln, durch Zeilenumbrüche
 * getrennt -- so steht in der Datenbank etwas, das man lesen kann, wenn man
 * einmal nachsehen muss.
 */
@Injectable({ providedIn: 'root' })
export class PreferencesService {
  private readonly http = inject(HttpClient);
  private readonly werte = signal<Record<string, string>>({});

  /** Beim Start und nach dem Anmelden. Ohne Anmeldung bleibt es leer. */
  async load(): Promise<void> {
    try {
      this.werte.set(
        await firstValueFrom(this.http.get<Record<string, string>>('/api/preferences')),
      );
    } catch {
      this.werte.set({});
    }
  }

  clear(): void {
    this.werte.set({});
  }

  /** Eine gemerkte Liste von Schlüsseln -- leer, solange nichts gemerkt ist. */
  liste(schluessel: string): string[] {
    const gemerkt = this.werte()[schluessel];
    return gemerkt ? gemerkt.split('\n') : [];
  }

  /**
   * Bringt eine gelieferte Liste in die gemerkte Reihenfolge.
   *
   * Was nicht gemerkt ist, bleibt hinten und untereinander in der gelieferten
   * Reihenfolge -- ein neues Modul taucht also auf, statt zu verschwinden,
   * und ein weggefallenes stört nicht.
   */
  sortiere<T>(schluessel: string, liste: readonly T[], id: (eintrag: T) => string): T[] {
    const gemerkt = this.werte()[schluessel];
    if (!gemerkt) {
      return [...liste];
    }

    const rang = new Map(gemerkt.split('\n').map((eintrag, index) => [eintrag, index]));

    return [...liste].sort(
      (a, b) =>
        (rang.get(id(a)) ?? Number.MAX_SAFE_INTEGER) - (rang.get(id(b)) ?? Number.MAX_SAFE_INTEGER),
    );
  }

  /**
   * Merkt eine Reihenfolge. Sie gilt sofort; geht das Speichern schief, fällt
   * sie sichtbar zurück -- lieber ein Sprung als eine Einstellung, die nur zu
   * gelten scheint und beim nächsten Anmelden weg ist.
   */
  async merke(schluessel: string, ids: readonly string[]): Promise<void> {
    const vorher = this.werte()[schluessel];
    const wert = ids.join('\n');
    this.werte.update((alle) => ({ ...alle, [schluessel]: wert }));

    try {
      await firstValueFrom(
        this.http.put(`/api/preferences/${encodeURIComponent(schluessel)}`, { value: wert }),
      );
    } catch {
      this.werte.update((alle) => {
        const zurueck = { ...alle };
        if (vorher === undefined) {
          delete zurueck[schluessel];
        } else {
          zurueck[schluessel] = vorher;
        }
        return zurueck;
      });
    }
  }

  /** Zurück auf die gelieferte Reihenfolge. */
  async vergiss(schluessel: string): Promise<void> {
    this.werte.update((alle) => {
      const ohne = { ...alle };
      delete ohne[schluessel];
      return ohne;
    });

    try {
      await firstValueFrom(this.http.delete(`/api/preferences/${encodeURIComponent(schluessel)}`));
    } catch {
      // Beim nächsten Laden steht die alte Reihenfolge wieder da. Das ist
      // ärgerlich, aber nichts, wofür man jemanden unterbricht.
    }
  }
}
