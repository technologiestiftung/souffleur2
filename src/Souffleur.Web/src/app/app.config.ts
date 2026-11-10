import {
  ApplicationConfig,
  LOCALE_ID,
  inject,
  provideAppInitializer,
  provideBrowserGlobalErrorListeners,
  provideZonelessChangeDetection,
} from '@angular/core';
import { registerLocaleData } from '@angular/common';
import localeDe from '@angular/common/locales/de';
import { provideHttpClient, withFetch, withInterceptors } from '@angular/common/http';
import { provideRouter, withComponentInputBinding } from '@angular/router';

import { routes } from './app.routes';
import { AuthService } from './core/auth.service';
import { sitzungInterceptor } from './core/sitzung.interceptor';
import { ModulesService } from './core/modules.service';
import { PreferencesService } from './core/preferences.service';
import { NotificationsService } from './core/notifications.service';
import { ThemeService } from './core/theme.service';

registerLocaleData(localeDe);

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideZonelessChangeDetection(),
    provideHttpClient(withFetch(), withInterceptors([sitzungInterceptor])),
    provideRouter(routes, withComponentInputBinding()),
    { provide: LOCALE_ID, useValue: 'de' },

    // Vor dem ersten Rendern: Thema setzen, Sitzung prüfen, Module laden.
    // Sonst blitzt kurz das helle Thema auf und der Guard schickt einen
    // angemeldeten Benutzer zur Anmeldung zurück.
    //
    // Alle inject() stehen VOR dem ersten await. Nach einem await ist der
    // Injection-Kontext verloren, und Angular bricht den Start mit NG0203 ab --
    // sichtbar nur als weiße Seite.
    provideAppInitializer(() => {
      inject(ThemeService);
      const auth = inject(AuthService);
      const modules = inject(ModulesService);
      const preferences = inject(PreferencesService);
      const notifications = inject(NotificationsService);

      return (async () => {
        await auth.restore();
        await modules.load();

        // Muss vor dem ersten Zeichnen da sein, sonst springt die Leiste
        // einmal von der gelieferten in die eigene Reihenfolge.
        await preferences.load();

        // Nicht abwarten: Die Glocke darf nachklingeln, die Seite soll stehen.
        if (auth.isSignedIn()) {
          void notifications.start();
        }
      })();
    }),
  ],
};
