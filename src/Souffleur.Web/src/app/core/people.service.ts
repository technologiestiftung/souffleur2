import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';

export interface Person {
  id: string;
  name: string;
  position: string | null;
}

interface ContactRow {
  id: string;
  kind: number;
  name: string;
  position: string | null;
  isActive: boolean;
}

/** Nur Personen, nicht Organisationen. */
const PERSON = 2;

/**
 * Das Adressbuch, soweit die Oberfläche es nebenbei braucht -- für die
 * Einteilung an einem Termin, die Zuständigkeit an einer Aufgabe, den Lieferanten
 * an einem Beleg.
 *
 * Eigener kleiner Dienst statt des ganzen Kontaktmoduls: Gebraucht werden hier
 * Name und Funktion, nicht Bankverbindung und Einwilligung.
 */
@Injectable({ providedIn: 'root' })
export class PeopleService {
  private readonly http = inject(HttpClient);

  private readonly loaded = signal<Person[]>([]);

  readonly people = this.loaded.asReadonly();

  /** Alle Kontakte, auch Organisationen -- Lieferanten sind selten Personen. */
  async all(): Promise<Person[]> {
    return (await this.rows())
      .filter((row) => row.isActive)
      .map((row) => ({ id: row.id, name: row.name, position: row.position }))
      .sort((left, right) => left.name.localeCompare(right.name, 'de'));
  }

  private async rows(): Promise<ContactRow[]> {
    try {
      return await firstValueFrom(this.http.get<ContactRow[]>('/api/contacts?take=500'));
    } catch {
      // Kontaktmodul nicht erreichbar.
      return [];
    }
  }

  async load(): Promise<Person[]> {
    try {
      const rows = await firstValueFrom(
        this.http.get<ContactRow[]>('/api/contacts?take=500'),
      );

      const found = rows
        .filter((row) => row.kind === PERSON && row.isActive)
        .map((row) => ({ id: row.id, name: row.name, position: row.position }))
        .sort((left, right) => left.name.localeCompare(right.name, 'de'));

      this.loaded.set(found);
      return found;
    } catch {
      // Kontaktmodul nicht erreichbar: dann gibt es niemanden einzuteilen.
      this.loaded.set([]);
      return [];
    }
  }
}
