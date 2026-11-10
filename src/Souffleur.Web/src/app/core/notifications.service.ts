import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { HubConnection, HubConnectionBuilder, HubConnectionState } from '@microsoft/signalr';
import { firstValueFrom } from 'rxjs';

export interface SouffleurNotification {
  id: string;
  kind: number;
  title: string;
  body: string | null;
  link: string | null;
  createdAt: string;
  isRead: boolean;
}

export interface NotificationSetting {
  kind: number;
  key: string;
  label: string;

  /** 0 aus, 1 sofort, 2 täglich gesammelt. */
  email: number;
}

/**
 * Die Glocke.
 *
 * Eigene Verbindung und nicht die des Chats: Der Chat verbindet sich nur,
 * wenn seine Leiste offen ist. Die Glocke soll klingeln, egal wo man ist.
 */
@Injectable({ providedIn: 'root' })
export class NotificationsService {
  private readonly http = inject(HttpClient);
  private connection: HubConnection | null = null;

  private readonly liste = signal<SouffleurNotification[]>([]);
  private readonly ungelesen = signal(0);

  readonly items = this.liste.asReadonly();
  readonly unread = this.ungelesen.asReadonly();
  readonly hasUnread = computed(() => this.ungelesen() > 0);

  /** Nach dem Anmelden: einmal laden, dann auf neue lauschen. */
  async start(): Promise<void> {
    await this.load();

    if (this.connection?.state === HubConnectionState.Connected) {
      return;
    }

    this.connection = new HubConnectionBuilder()
      .withUrl('/hubs/notifications')
      .withAutomaticReconnect()
      .build();

    this.connection.on('notification', (neu: SouffleurNotification) => {
      this.liste.update((alle) => [neu, ...alle].slice(0, 50));
      this.ungelesen.update((n) => n + 1);
    });

    // Nach einer Unterbrechung kann etwas verpasst worden sein -- dann neu holen.
    this.connection.onreconnected(() => void this.load());

    try {
      await this.connection.start();
    } catch {
      // Ohne Echtzeit bleibt die Glocke beim Stand des letzten Ladens. Das
      // ist kein Grund, den Start der Seite aufzuhalten.
    }
  }

  async stop(): Promise<void> {
    await this.connection?.stop();
    this.connection = null;
    this.liste.set([]);
    this.ungelesen.set(0);
  }

  async load(): Promise<void> {
    try {
      const result = await firstValueFrom(
        this.http.get<{ items: SouffleurNotification[]; unread: number }>('/api/notifications'),
      );
      this.liste.set(result.items);
      this.ungelesen.set(result.unread);
    } catch {
      this.liste.set([]);
      this.ungelesen.set(0);
    }
  }

  async markRead(item: SouffleurNotification): Promise<void> {
    if (item.isRead) {
      return;
    }
    this.liste.update((alle) => alle.map((n) => (n.id === item.id ? { ...n, isRead: true } : n)));
    this.ungelesen.update((n) => Math.max(0, n - 1));
    await firstValueFrom(this.http.post(`/api/notifications/${item.id}/read`, {}));
  }

  async markAllRead(): Promise<void> {
    this.liste.update((alle) => alle.map((n) => ({ ...n, isRead: true })));
    this.ungelesen.set(0);
    await firstValueFrom(this.http.post('/api/notifications/read-all', {}));
  }

  settings(): Promise<{ mailReady: boolean; settings: NotificationSetting[] }> {
    return firstValueFrom(
      this.http.get<{ mailReady: boolean; settings: NotificationSetting[] }>('/api/notifications/settings'),
    );
  }

  async saveSettings(settings: { kind: number; email: number }[]): Promise<void> {
    await firstValueFrom(this.http.put('/api/notifications/settings', settings));
  }
}
