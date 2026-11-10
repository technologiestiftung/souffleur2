import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

/** Eine Gruppe zum Auswählen -- ohne Mitglieder und Rollen. */
export interface GroupSummary {
  id: string;
  name: string;
  note: string | null;
}

/**
 * Die Gruppen des Hauses, so weit sie jeder sehen darf: Name genügt, um eine
 * Aufgabe an „die Technik" zu geben. Wer wozu gehört, liegt beim
 * AccessService und verlangt das Verwaltungsrecht.
 */
@Injectable({ providedIn: 'root' })
export class GroupsService {
  private readonly http = inject(HttpClient);

  active(): Promise<GroupSummary[]> {
    return firstValueFrom(this.http.get<GroupSummary[]>('/api/groups'));
  }

  mine(): Promise<GroupSummary[]> {
    return firstValueFrom(this.http.get<GroupSummary[]>('/api/groups/mine'));
  }

  /** Die Kontakte hinter den Konten einer Gruppe -- für den Dienstplan. */
  contactsOf(groupId: string): Promise<string[]> {
    return firstValueFrom(this.http.get<string[]>(`/api/groups/${groupId}/contacts`));
  }
}
