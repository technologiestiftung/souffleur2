import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { AuthService } from '../../core/auth.service';
import { MeldungComponent } from '../../shared/meldung.component';

@Component({
  selector: 'souffleur-login',
  imports: [FormsModule, MeldungComponent],
  templateUrl: './login.component.html',
})
export class LoginComponent {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  protected readonly email = signal('');
  protected readonly password = signal('');
  protected readonly rememberMe = signal(false);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  // Kommt der Interceptor hierher, weil die Sitzung abgelaufen ist (etwa nach
  // einem Neustart des Servers), soll das dastehen -- nicht ein leeres Formular.
  constructor() {
    if (new URLSearchParams(location.search).has('abgelaufen')) {
      this.error.set('Die Anmeldung ist abgelaufen. Bitte noch einmal anmelden.');
    }
  }

  protected async submit(): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    try {
      await this.auth.signIn(this.email(), this.password(), this.rememberMe());
      await this.router.navigate(['/uebersicht']);
    } catch {
      this.error.set('Anmeldung fehlgeschlagen. Adresse und Passwort prüfen.');
    } finally {
      this.busy.set(false);
    }
  }
}
