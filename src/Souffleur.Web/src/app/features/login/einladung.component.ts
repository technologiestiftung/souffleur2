import { HttpClient } from '@angular/common/http';
import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { messagesFrom } from '../../shared/fehler';
import { MeldungComponent } from '../../shared/meldung.component';

/**
 * Die Einladung annehmen: Passwort festlegen.
 *
 * Steht außerhalb der Shell wie die Anmeldung -- wer eingeladen ist, hat noch
 * kein Passwort. Das Passwort wird zweimal eingegeben, weil es sonst niemand
 * merkt, wenn es sich vertippt hat: Es gibt kein altes, mit dem man es prüfen
 * könnte.
 */
@Component({
  selector: 'souffleur-einladung',
  imports: [FormsModule, RouterLink, MeldungComponent],
  template: `
    <main class="grid min-h-dvh place-items-center bg-stone-100 p-4 dark:bg-stone-900">
      <form
        class="w-full max-w-sm space-y-5 rounded-xl bg-white p-8 shadow-sm dark:bg-stone-800"
        (ngSubmit)="submit()"
      >
        <div>
          <h1 class="text-2xl font-semibold text-stone-900 dark:text-stone-50">souffleur</h1>
          <p class="mt-1 text-sm text-stone-500 dark:text-stone-400">Einladung</p>
        </div>

        @switch (lage()) {
          @case ('prueft') {
            <p class="text-sm text-stone-600 dark:text-stone-300">Einen Moment…</p>
          }
          @case ('ungueltig') {
            <souffleur-meldung
              texte="Der Link ist abgelaufen oder schon benutzt. Bitte die Verwaltung des Hauses um einen neuen."
            />
            <a routerLink="/anmelden" class="block text-sm text-stone-700 underline dark:text-stone-300"
              >Zur Anmeldung</a
            >
          }
          @case ('fertig') {
            <p class="text-sm text-stone-700 dark:text-stone-300">
              Das Passwort ist festgelegt. Ab jetzt meldest du dich mit
              <strong>{{ email() }}</strong> an.
            </p>
            <a
              routerLink="/anmelden"
              class="block w-full rounded-md bg-stone-900 px-4 py-2 text-center font-medium text-white no-underline dark:bg-stone-50 dark:text-stone-900"
              >Zur Anmeldung</a
            >
          }
          @default {
            <p class="text-sm text-stone-700 dark:text-stone-300">
              Hallo {{ name() }}, lege hier dein Passwort fest — mindestens 12 Zeichen, mit Groß-
              und Kleinbuchstaben und einer Ziffer.
            </p>

            @if (fehler().length > 0) {
              <souffleur-meldung [texte]="fehler()" />
            }

            <!-- Für den Passwortmanager: Er merkt sich das Passwort zu dieser Adresse. -->
            <input type="email" name="username" autocomplete="username" [value]="email()" hidden />

            <label class="block space-y-1">
              <span class="text-sm font-medium text-stone-700 dark:text-stone-300">Neues Passwort</span>
              <input
                type="password"
                name="password"
                autocomplete="new-password"
                required
                [ngModel]="passwort()"
                (ngModelChange)="passwort.set($event)"
                class="w-full rounded-md border border-stone-300 px-3 py-2 dark:border-stone-600 dark:bg-stone-700 dark:text-stone-50"
              />
            </label>

            <label class="block space-y-1">
              <span class="text-sm font-medium text-stone-700 dark:text-stone-300">Noch einmal</span>
              <input
                type="password"
                name="repeat"
                autocomplete="new-password"
                required
                [ngModel]="wiederholung()"
                (ngModelChange)="wiederholung.set($event)"
                class="w-full rounded-md border border-stone-300 px-3 py-2 dark:border-stone-600 dark:bg-stone-700 dark:text-stone-50"
              />
            </label>

            <button
              type="submit"
              [disabled]="busy()"
              class="w-full rounded-md bg-stone-900 px-4 py-2 font-medium text-white disabled:opacity-50 dark:bg-stone-50 dark:text-stone-900"
            >
              {{ busy() ? 'Einen Moment…' : 'Passwort festlegen' }}
            </button>
          }
        }
      </form>
    </main>
  `,
})
export class EinladungComponent implements OnInit {
  private readonly http = inject(HttpClient);
  private readonly route = inject(ActivatedRoute);

  protected readonly lage = signal<'prueft' | 'offen' | 'ungueltig' | 'fertig'>('prueft');
  protected readonly name = signal('');
  protected readonly email = signal('');
  protected readonly passwort = signal('');
  protected readonly wiederholung = signal('');
  protected readonly fehler = signal<string[]>([]);
  protected readonly busy = signal(false);

  private user = '';
  private token = '';

  async ngOnInit(): Promise<void> {
    const params = this.route.snapshot.queryParamMap;
    this.user = params.get('user') ?? '';
    this.token = params.get('token') ?? '';

    try {
      const info = await firstValueFrom(
        this.http.get<{ name: string; email: string }>('/api/account/invitation', {
          params: { user: this.user, token: this.token },
        }),
      );
      this.name.set(info.name);
      this.email.set(info.email);
      this.lage.set('offen');
    } catch {
      this.lage.set('ungueltig');
    }
  }

  protected async submit(): Promise<void> {
    this.fehler.set([]);
    if (this.passwort() !== this.wiederholung()) {
      this.fehler.set(['Die beiden Eingaben sind nicht gleich.']);
      return;
    }

    this.busy.set(true);
    try {
      await firstValueFrom(
        this.http.post('/api/account/invitation', {
          user: this.user,
          token: this.token,
          password: this.passwort(),
        }),
      );
      this.lage.set('fertig');
    } catch (error) {
      if ((error as { status?: number })?.status === 410) {
        this.lage.set('ungueltig');
      } else {
        this.fehler.set(messagesFrom(error));
      }
    } finally {
      this.busy.set(false);
    }
  }
}
