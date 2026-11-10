import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { ChatService } from './chat.service';
import { ModulesService } from './modules.service';
import { PreferencesService } from './preferences.service';
import { NotificationsService } from './notifications.service';

export interface CurrentUser {
  id: string;
  email: string;
  displayName: string;
  roles: string[];

  /** Die Namen der Rechte, etwa „AccountsManage". Nur zum Ausblenden -- geprüft wird am Server. */
  permissions: string[];
}

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly modules = inject(ModulesService);
  private readonly chat = inject(ChatService);
  private readonly preferences = inject(PreferencesService);
  private readonly notifications = inject(NotificationsService);
  private readonly current = signal<CurrentUser | null>(null);

  readonly user = this.current.asReadonly();
  readonly isSignedIn = computed(() => this.current() !== null);

  /** Beim Start einmal fragen, ob das Cookie noch gilt. */
  async restore(): Promise<void> {
    try {
      this.current.set(await firstValueFrom(this.http.get<CurrentUser>('/api/account/me')));
    } catch {
      this.current.set(null);
    }
  }

  async signIn(email: string, password: string, rememberMe: boolean): Promise<void> {
    const user = await firstValueFrom(
      this.http.post<CurrentUser>('/api/account/login', { email, password, rememberMe }),
    );
    this.current.set(user);

    // Die Modulliste beim Start kam ohne Anmeldung und war deshalb leer.
    // Ohne das Nachladen bliebe die Reiterleiste nach dem Anmelden leer.
    await this.modules.load();
    await this.preferences.load();
    void this.notifications.start();
  }

  async signOut(): Promise<void> {
    await firstValueFrom(this.http.post('/api/account/logout', {}));
    this.current.set(null);
    this.modules.clear();
    this.preferences.clear();
    await this.notifications.stop();
    await this.chat.disconnect();
  }

  hasRole(role: string): boolean {
    return this.current()?.roles.includes(role) ?? false;
  }

  /**
   * Ob die angemeldete Person ein Recht hat. Blendet aus, was sie ohnehin
   * nicht darf -- eine Sicherung ist das nicht, die macht der Server.
   */
  can(permission: string): boolean {
    return this.current()?.permissions?.includes(permission) ?? false;
  }
}
