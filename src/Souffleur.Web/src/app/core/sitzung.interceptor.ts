import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { catchError, throwError } from 'rxjs';

/**
 * Ist die Anmeldung abgelaufen, gehört man auf die Anmeldeseite -- nicht vor
 * einen Bogen, der „Das hat nicht geklappt“ sagt und den Grund verschweigt.
 *
 * Ein vollständiges Neuladen statt Router: Dann fragt die Anwendung beim Start
 * von selbst, ob noch jemand angemeldet ist, und räumt Chat, Glocke und
 * Module auf, ohne dass dieser Interceptor den AuthService kennen muss (der
 * selbst über HttpClient läuft -- das wäre ein Kreis).
 *
 * Die Pfade unter /api/account bleiben außen vor: Eine falsche Anmeldung
 * antwortet ebenfalls mit 401, und „me“ fragt beim Start genau das ab.
 * Ebenso die Seiten, die ohne Anmeldung da sein müssen -- wer eine Einladung
 * annimmt, hat noch keine, und ohne diese Ausnahme schickte ihn die erste
 * Abfrage beim Start auf die Anmeldung.
 */
const OHNE_ANMELDUNG = ['/anmelden', '/einladung'];

export const sitzungInterceptor: HttpInterceptorFn = (req, next) =>
  next(req).pipe(
    catchError((error: unknown) => {
      if (
        error instanceof HttpErrorResponse &&
        error.status === 401 &&
        !req.url.startsWith('/api/account/') &&
        !OHNE_ANMELDUNG.some((pfad) => location.pathname.startsWith(pfad))
      ) {
        location.assign('/anmelden?abgelaufen=1');
      }
      return throwError(() => error);
    }),
  );
