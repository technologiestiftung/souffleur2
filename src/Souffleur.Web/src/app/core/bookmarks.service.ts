import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

/** Woran ein Lesezeichen hängen kann -- dieselben Namen wie am Server. */
export type BookmarkTarget =
  | 'Contact'
  | 'Task'
  | 'Event'
  | 'JournalEntry'
  | 'Voucher'
  | 'TourStop'
  | 'Production'
  | 'Document'
  | 'Tour';

export interface Bookmark {
  id: string;
  target: number;
  targetId: string;
  url: string;
  label: string | null;
  sortOrder: number;
}

/** Ein Lesezeichen, das es noch nicht gibt -- am Bogen eines Gegenstands, der selbst noch nicht gespeichert ist. */
export interface BookmarkDraft {
  url: string;
  label: string | null;
}

/**
 * Lesezeichen: Adressen im Netz an jedem Gegenstand des Hauses. Der Dienst
 * kennt nur Art und Id; was der Gegenstand ist, weiß die Seite, die ihn zeigt.
 */
@Injectable({ providedIn: 'root' })
export class BookmarksService {
  private readonly http = inject(HttpClient);

  list(target: BookmarkTarget, targetId: string): Promise<Bookmark[]> {
    return firstValueFrom(this.http.get<Bookmark[]>(`/api/bookmarks/${target}/${targetId}`));
  }

  add(target: BookmarkTarget, targetId: string, draft: BookmarkDraft): Promise<Bookmark> {
    return firstValueFrom(this.http.post<Bookmark>(`/api/bookmarks/${target}/${targetId}`, draft));
  }

  update(id: string, draft: BookmarkDraft): Promise<Bookmark> {
    return firstValueFrom(this.http.put<Bookmark>(`/api/bookmarks/${id}`, draft));
  }

  async remove(id: string): Promise<void> {
    await firstValueFrom(this.http.delete(`/api/bookmarks/${id}`));
  }

  /** Was in der Liste steht, wenn kein Stichwort da ist: der Host ohne www. */
  static hostOf(url: string): string {
    try {
      return new URL(url).hostname.replace(/^www\./, '');
    } catch {
      return url;
    }
  }
}
