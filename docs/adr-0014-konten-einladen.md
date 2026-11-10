# ADR-0014: Konten werden eingeladen, nicht mit Passwort angelegt

**Stand:** 10.11.2026 · **Status:** entschieden · **Gilt für:** souffleur 2

## Anlass

Bis hierhin gab es genau ein Konto: das erste, aus der Konfiguration beim
allerersten Start. Weitere Konten ließen sich nur über die Datenbank anlegen. Das
war die Lücke vor dem ersten Haus — und zugleich der Grund, warum Kontakte aus dem
Adressbuch nicht in den Chat kamen.

## Entscheidung

**Die Verwaltung legt ein Konto ohne Passwort an. souffleur erzeugt einen
Einladungslink, und die Person setzt ihr Passwort selbst.**

- Angelegt wird unter Einstellungen → Zugänge → Personen („+ Konto“), mit Vorname,
  Nachname, E-Mail-Adresse und den eigenen Rollen. Recht: `AccountsManage`.
- Der Link ist ein Passwort-Rücksetz-Token von ASP.NET Identity. Er gilt sieben Tage
  (`InvitationEndpoints.Gueltigkeit`) und nur einmal: Das Setzen des Passworts ändert
  den Sicherheitsstempel, und damit ist das Token verbraucht.
- „Neuer Link“ erzeugt einen frischen und macht den alten ungültig. Bei einem Konto,
  das schon ein Passwort hat, ist das der Weg für ein vergessenes Passwort.
- Ist ein Mailserver eingetragen (`Souffleur__Mail__*`), geht der Link per Mail
  hinaus. Sonst zeigt die Oberfläche ihn zum Weitergeben. Der Link steht in beiden
  Fällen im Fenster, weil Mails im Spamordner landen.
- Die Seite `/einladung` ist ohne Anmeldung erreichbar und hängt an derselben
  Bremse wie die Anmeldung (`anmeldung`, 50 Versuche je fünf Minuten und Adresse).
- **Sperren statt Löschen.** Ein gesperrtes Konto meldet sich nicht mehr an, laufende
  Sitzungen enden mit der nächsten Prüfung des Sicherheitsstempels (höchstens zwei
  Minuten). Gelöscht wird nie: Buchungen, Zeiten und Nachrichten hängen am Konto.
  Das eigene Konto lässt sich nicht sperren.

## Verworfen

**Die Verwaltung vergibt ein Startpasswort.** Am einfachsten zu bauen, aber dann
kennen zwei Leute das Passwort. Es geht durch einen Chat oder eine Mail, und
„beim ersten Anmelden ändern“ wird im Alltag übergangen.

**Selbst registrieren mit Freigabe.** Eine offene Registrierungsseite an einem
Dienst, der aus dem Netz erreichbar ist, lädt zum Ausprobieren ein. Für die Handvoll
Leute eines Hauses lohnt das nicht.

**Anmeldung über einen fremden Dienst (Microsoft, Google).** Die Häuser nutzen
nichts Einheitliches, und souffleur soll ohne Verbindung nach außen laufen
(ADR-0008).

## Was bewusst fehlt

- Kein „Passwort vergessen“ auf der Anmeldeseite. Das ginge nur per Mail, und ohne
  Mailserver bliebe es ein Knopf ohne Wirkung. Die Verwaltung erzeugt einen neuen
  Link.
- Keine Verbindung zum Kontakt beim Anlegen. Konto und Kontakt werden wie bisher im
  Kontaktbogen verbunden („Anmeldekonto“).
- Keine Zwei-Faktor-Anmeldung.
