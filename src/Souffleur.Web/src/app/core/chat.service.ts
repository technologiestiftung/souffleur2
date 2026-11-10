import { HttpClient } from '@angular/common/http';
import { Injectable, Injector, computed, inject, signal } from '@angular/core';
import {
  HubConnection,
  HubConnectionBuilder,
  HubConnectionState,
  LogLevel,
} from '@microsoft/signalr';
import { firstValueFrom } from 'rxjs';
import { AuthService } from './auth.service';
import { GroupSummary, GroupsService } from './groups.service';

export interface ChatPerson {
  id: string;
  name: string;
}

export interface Conversation {
  id: string;
  kind: number;
  name: string | null;
  topic: string | null;
  subjectType: number;
  subjectLabel: string;
  subjectId: string | null;
  isPrivate: boolean;
  isArchived: boolean;
  lastMessageAt: string | null;
  unread: number;
  participants: ChatPerson[];
  createdBy: string | null;
}

export interface ChatMessage {
  id: string;
  conversationId: string;
  authorId: string;
  authorName: string;
  text: string;
  isRetracted: boolean;
  createdAt: string;
  editedAt: string | null;
  inReplyToId: string | null;
}

export interface Participant {
  userId: string;
  name: string;
  joinedAt: string;
}

/** Wer oder was hinter einem @ stehen kann: eine Person des Hauses oder eine Gruppe. */
export interface Mentionable {
  id: string;
  name: string;
  kind: 'person' | 'gruppe';
}

export const ConversationKind = { Channel: 1, Direct: 2, Attached: 3 } as const;

@Injectable({ providedIn: 'root' })
export class ChatService {
  private readonly http = inject(HttpClient);
  private readonly injector = inject(Injector);
  private readonly groupsService = inject(GroupsService);

  /**
   * Erst beim Gebrauch geholt: Die Anmeldung trennt beim Abmelden die
   * Chat-Verbindung und braucht dafür diesen Dienst -- umgekehrt braucht der
   * Chat die Anmeldung, um das eigene Konto zu kennen. Beide beim Erzeugen
   * zu verlangen wäre ein Kreis.
   */
  private get auth(): AuthService {
    return this.injector.get(AuthService);
  }

  private readonly conversationList = signal<Conversation[]>([]);
  private readonly messageList = signal<ChatMessage[]>([]);
  private readonly openId = signal<string | null>(null);
  private readonly peopleList = signal<ChatPerson[]>([]);
  private readonly groupList = signal<GroupSummary[]>([]);
  private connection: HubConnection | null = null;

  /** Alles, was sichtbar ist -- die Leiste trennt selbst in laufend und archiviert. */
  readonly conversations = computed(() => this.conversationList().filter((c) => !c.isArchived));
  readonly archived = computed(() => this.conversationList().filter((c) => c.isArchived));
  readonly messages = this.messageList.asReadonly();
  readonly openConversationId = this.openId.asReadonly();
  readonly people = this.peopleList.asReadonly();
  readonly groups = this.groupList.asReadonly();

  readonly open = computed(
    () => this.conversationList().find((c) => c.id === this.openId()) ?? null,
  );

  /** Für die Zahl am zugeklappten Chat. */
  readonly unreadTotal = computed(() => this.conversations().reduce((sum, c) => sum + c.unread, 0));

  /** Personen und Gruppen für die Vorschlagsliste hinter dem @, ohne mich selbst. */
  readonly mentionables = computed<Mentionable[]>(() => {
    const me = this.auth.user()?.id;
    return [
      ...this.peopleList()
        .filter((p) => p.id !== me)
        .map((p) => ({ id: p.id, name: p.name, kind: 'person' as const })),
      ...this.groupList().map((g) => ({ id: g.id, name: g.name, kind: 'gruppe' as const })),
    ];
  });

  /**
   * Eine Unterhaltung zum Anzeigen benennen -- Kanäle heißen, Angeheftetes
   * zeigt seinen Gegenstand, eine Direktnachricht die andere Person.
   */
  title(conversation: Conversation): string {
    if (conversation.name) {
      return conversation.name;
    }
    if (conversation.subjectLabel) {
      return `${conversation.subjectLabel}`;
    }
    if (conversation.kind === ConversationKind.Direct) {
      const others = this.others(conversation);
      return others.length > 0 ? others.map((p) => p.name).join(', ') : 'Direktnachricht';
    }
    return 'Unterhaltung';
  }

  /** Die anderen in einer Unterhaltung -- für Direktnachrichten der Name im Titel. */
  others(conversation: Conversation): ChatPerson[] {
    const me = this.auth.user()?.id;
    return conversation.participants.filter((p) => p.id !== me);
  }

  /** Löschen darf, wer angelegt hat, und die Verwaltung -- und nur Kanäle. */
  mayDelete(conversation: Conversation): boolean {
    if (conversation.kind !== ConversationKind.Channel) {
      return false;
    }
    return this.auth.can('HouseManage') || conversation.createdBy === this.auth.user()?.id;
  }

  async load(): Promise<void> {
    try {
      this.conversationList.set(
        await firstValueFrom(
          this.http.get<Conversation[]>('/api/chat/conversations?includeArchived=true'),
        ),
      );
    } catch {
      // Modul abgeschaltet oder nicht angemeldet: dann gibt es nichts zu zeigen.
      this.conversationList.set([]);
    }
  }

  /** Die Auswahl für Teilnehmer und Erwähnungen -- einmal pro Aufklappen genügt. */
  async loadPeople(): Promise<void> {
    try {
      const [people, groups] = await Promise.all([
        firstValueFrom(this.http.get<ChatPerson[]>('/api/chat/people')),
        this.groupsService.active(),
      ]);
      this.peopleList.set(people);
      this.groupList.set(groups);
    } catch {
      this.peopleList.set([]);
      this.groupList.set([]);
    }
  }

  async openConversation(id: string): Promise<void> {
    const previous = this.openId();
    if (previous && previous !== id) {
      await this.connection?.invoke('Leave', previous).catch(() => undefined);
    }

    this.openId.set(id);
    this.messageList.set(
      await firstValueFrom(this.http.get<ChatMessage[]>(`/api/chat/conversations/${id}/messages`)),
    );

    await this.ensureConnected();
    await this.connection?.invoke('Join', id).catch(() => undefined);

    await firstValueFrom(this.http.post(`/api/chat/conversations/${id}/read`, {}));
    this.markReadLocally(id);
  }

  close(): void {
    const id = this.openId();
    if (id) {
      this.connection?.invoke('Leave', id).catch(() => undefined);
    }
    this.openId.set(null);
    this.messageList.set([]);
  }

  async post(text: string, mentions: Mentionable[] = []): Promise<void> {
    const id = this.openId();
    if (!id || !text.trim()) {
      return;
    }

    const message = await firstValueFrom(
      this.http.post<ChatMessage>(`/api/chat/conversations/${id}/messages`, {
        text,
        mentions: mentions.filter((m) => m.kind === 'person').map((m) => m.id),
        groupMentions: mentions.filter((m) => m.kind === 'gruppe').map((m) => m.id),
      }),
    );

    // Sofort anzeigen. Kommt dieselbe Nachricht gleich über die Verbindung
    // zurück, fängt die Prüfung auf die Id sie ab.
    this.appendIfNew(message);
  }

  async createChannel(name: string, isPrivate = false): Promise<Conversation> {
    const created = await firstValueFrom(
      this.http.post<Conversation>('/api/chat/conversations', { name, isPrivate }),
    );
    await this.load();
    return created;
  }

  /** Die Unterhaltung zu einem Gegenstand -- sie entsteht beim ersten Aufruf. */
  async forSubject(subject: string, subjectId: string): Promise<Conversation> {
    const conversation = await firstValueFrom(
      this.http.post<Conversation>(`/api/chat/subjects/${subject}/${subjectId}`, {}),
    );
    await this.load();
    return conversation;
  }

  /** Die Direktnachricht mit einer Person -- es gibt sie genau einmal. */
  async direct(userId: string): Promise<Conversation> {
    const conversation = await firstValueFrom(
      this.http.post<Conversation>(`/api/chat/conversations/direct/${userId}`, {}),
    );
    await this.load();
    return conversation;
  }

  participants(id: string): Promise<Participant[]> {
    return firstValueFrom(
      this.http.get<Participant[]>(`/api/chat/conversations/${id}/participants`),
    );
  }

  async addParticipants(id: string, userIds: string[], groupIds: string[]): Promise<void> {
    await firstValueFrom(
      this.http.post(`/api/chat/conversations/${id}/participants`, { userIds, groupIds }),
    );
    await this.load();
  }

  /** Jemanden herausnehmen -- oder sich selbst, dann ist es Verlassen. */
  async removeParticipant(id: string, userId: string): Promise<void> {
    await firstValueFrom(this.http.delete(`/api/chat/conversations/${id}/participants/${userId}`));
    await this.load();
  }

  async archive(id: string): Promise<void> {
    await firstValueFrom(this.http.post(`/api/chat/conversations/${id}/archive`, {}));
    await this.load();
  }

  async unarchive(id: string): Promise<void> {
    await firstValueFrom(this.http.post(`/api/chat/conversations/${id}/unarchive`, {}));
    await this.load();
  }

  async remove(id: string): Promise<void> {
    await firstValueFrom(this.http.delete(`/api/chat/conversations/${id}`));
    if (this.openId() === id) {
      this.close();
    }
    await this.load();
  }

  async disconnect(): Promise<void> {
    await this.connection?.stop().catch(() => undefined);
    this.connection = null;
    this.conversationList.set([]);
    this.messageList.set([]);
    this.openId.set(null);
  }

  /**
   * Baut die Verbindung beim ersten Bedarf auf, nicht beim Start der Anwendung:
   * Wer nie in den Chat sieht, braucht auch keine offene Verbindung.
   */
  async ensureConnected(): Promise<void> {
    if (this.connection?.state === HubConnectionState.Connected) {
      return;
    }

    if (!this.connection) {
      this.connection = new HubConnectionBuilder()
        .withUrl('/hubs/chat')
        .withAutomaticReconnect()
        .configureLogging(LogLevel.Warning)
        .build();

      this.connection.on('messagePosted', (message: ChatMessage) => {
        if (message.conversationId === this.openId()) {
          this.appendIfNew(message);
        } else {
          this.bumpUnread(message.conversationId);
        }
      });

      this.connection.on('messageRetracted', (id: string) => {
        this.messageList.update((messages) =>
          messages.map((m) => (m.id === id ? { ...m, isRetracted: true, text: '' } : m)),
        );
      });

      // Jemand hat mich aufgenommen, herausgenommen, archiviert oder gelöscht --
      // oder mir geschrieben, ohne dass ich die Unterhaltung offen hatte. Die
      // Liste neu holen ist einfacher als jeden Fall einzeln nachzuspielen.
      this.connection.on('conversationChanged', async (id: string) => {
        await this.load();
        if (this.openId() === id && !this.open()) {
          this.close();
        }
      });
    }

    if (this.connection.state === HubConnectionState.Disconnected) {
      await this.connection.start().catch(() => undefined);
    }
  }

  private appendIfNew(message: ChatMessage): void {
    this.messageList.update((messages) =>
      messages.some((m) => m.id === message.id) ? messages : [...messages, message],
    );
  }

  private bumpUnread(conversationId: string): void {
    this.conversationList.update((list) =>
      list.map((c) => (c.id === conversationId ? { ...c, unread: c.unread + 1 } : c)),
    );
  }

  private markReadLocally(conversationId: string): void {
    this.conversationList.update((list) =>
      list.map((c) => (c.id === conversationId ? { ...c, unread: 0 } : c)),
    );
  }
}
