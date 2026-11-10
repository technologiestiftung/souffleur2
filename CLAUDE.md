# CLAUDE.md — souffleur 2

Verwaltungssoftware für freie Theater und Kulturhäuser: Finanzen, Personal,
Kalender, Aufgaben, Kontakte, Dokumente, Chat. .NET 10 (Minimal APIs, EF Core,
SQLite) und Angular 20 (Signals, Standalone, Tailwind v4). Läuft beim Haus
(Synology/Mini-PC) als Container — siehe `docs/adr-0008-betrieb-im-haus.md`.

**Status:** Seit 10.11.2026 veröffentlicht und abgeschlossen, keine
Weiterentwicklung durch die Technologiestiftung. Diese Datei beschreibt den
Schlussstand für alle, die souffleur weiterführen. Notizen einer einzelnen
Arbeitsumgebung gehören in `CLAUDE.local.md` (nicht im Repository).

## Arbeitsweise

- **Vor jedem Commit `dotnet test` und die Browsertests.** Eine CI gibt es
  im veröffentlichten Stand nicht mehr.
- **Erst die Entwicklungsumgebung lauffähig machen, dann committen oder
  ausrollen.** Ein laufender Dev-Server zieht neue Endpunkte und Migrationen
  nicht nach: neu starten und prüfen, dass der neue Stand wirklich antwortet.
- **Commit-Texte deutsch und ausführlich mit dem Warum.** Im Commit-Text
  ae/oe/ue, in C#-Kommentaren ebenso (Hausstil), in TS/HTML echte Umlaute.
- **Kommentare erklären Gründe, nicht Mechanik.** Dichte und Ton wie im
  umliegenden Code.
- Größere Entscheidungen als ADR unter `docs/`.

## Befehle

```bash
./dev.sh                     # Backend :5080, Oberfläche :4200 (--reset: DB neu)
./dev.sh --haus <name>       # DB neu mit dem Profil eines Hauses; ohne Name: Liste
dotnet test                  # alle .NET-Tests
cd src/Souffleur.Web && npm run build && npm run e2e   # Browsertests (eigener Server, Port je Worktree)
dotnet ef migrations add <Name> --project src/Souffleur.Infrastructure \
  --startup-project src/Souffleur.Api --output-dir Persistence/Migrations
```

Hausprofile für `--haus` liegen in `data/haeuser/<name>.json` (gitignored,
Kundendaten; Aufbau in `Infrastructure/Persistence/HouseProfile.cs`). Ohne
Profil gilt der eingebaute Bestand.

Lokale Konten (nur `appsettings.Development.json`, nie im Image):
`admin@localhost` und `kollege@localhost` (verbunden mit „Schröder, Ben"),
beide `Entwicklung1234`. Zwei Personen braucht man für Glocke und Chat —
wer selbst handelt, bekommt keinen Hinweis.

## Stand

516 .NET-Tests, 118 Browsertests grün (10.11.2026), darunter die
Barrierefreiheit aller Hauptseiten. Auf 1280×720 fuellten die Kennzahlen die Personal-Seite, die Dienste-Liste
(`flex-1 overflow-auto` unter `h-full`) bekam keine Hoehe und war nicht
erreichbar. Je nachdem, ob die Kacheln vor dem Klick geladen waren, wackelte
der Test. Behoben mit `min-h-96` am Scrollbereich, ebenso Kontakte,
Buchungen und Ticketing (10.11.2026). Neue Seiten mit Kennzahlen und Liste
darunter brauchen dieselbe Mindesthöhe.

Seit Anfang November dazugekommen:

- **Chat: Antworten, Ändern, Kanal je Gruppe** (10.11.2026). Antworten mit
  Zitat über `InReplyToId` (nur innerhalb derselben Unterhaltung, sonst
  liesse sich ein fremder geschlossener Kanal anzitieren); Klick aufs Zitat
  springt zur Nachricht. Eigene Nachricht ändern (`PUT /api/chat/messages/{id}`,
  Hub `messageEdited`, „bearbeitet“; Erwähnungen bleiben, wie sie beim Senden
  waren). Ein Kanal kann einer Gruppe folgen (`Conversation.GroupId`, ohne
  Fremdschlüssel, Migration `KanalJeGruppe` ohne Tabellenumbau): genau ihre
  Mitglieder sind drin, `GroupChannels.SyncAsync` gleicht beim Anlegen und bei
  jeder Gruppenänderung ab; von Hand aufnehmen/herausnehmen geht dort nicht.

- **Termine bearbeiten** (10.11.2026): Im Tagesfenster hat jeder Termin aus
  dem Kalender „Bearbeiten“; derselbe `souffleur-event-dialog` mit Eingabe
  `edit` (eine `Occurrence`). Einzeltermin: `PUT /api/calendar/events/{id}`.
  Aus einer Serie fragt der Bogen „Nur dieser Termin“ (Abweichung über
  `PUT …/series/{id}/occurrences/{start}`, die Räume und Leute der Serie gehen
  mit) oder „Ganze Serie“ (`PUT …/series/{id}`, einzeln geänderte bleiben).
  Absagen über den Status, Löschen für Einzeltermin und ganze Serie mit
  Rückfrage im Knopf. Dokumente (`shared/dokumente-am-ziel.component.ts`) und
  Lesezeichen am Termin, sobald er eine Id hat. `GET` für Termin und Serie
  lädt jetzt die Einteilung mit — vorher fehlte sie, und Speichern hätte alle
  Eingeteilten entfernt.
- **Kontenplan** (10.11.2026): Einstellungen → Kontenplan
  (`features/settings/kontenplan.component`, `core/kontenplan.service.ts`).
  Liste mit Filter, Anlegen/Ändern/Stilllegen (`PUT /api/accounting/accounts/{id}`,
  Nummer unveränderlich, nur Ziffern), CSV vom Steuerbüro mit Vorschau
  (`parseKontenCsv`: `Nummer;Name;Art;Steuerschlüssel`, Art wird gewählt, nie
  geraten), Kostenstellen anlegen. Lesen mit `FinanceRead`, Ändern mit
  `FinanceBook`.
- **Konten einladen** (ADR-0014, 10.11.2026): Zugänge → Personen → „+ Konto“
  legt ohne Passwort an und zeigt einen Einladungslink (per Mail, wenn ein
  Mailserver eingetragen ist). `/einladung` setzt das Passwort, ohne Anmeldung
  erreichbar (auch im `sitzungInterceptor` ausgenommen). „Neuer Link“,
  „Sperren“/„Freigeben“ je Person. API `POST /api/access/users`,
  `…/{id}/invitation`, `PUT …/{id}/active`, `GET`/`POST /api/account/invitation`.

- **Ordner der Synology als Ablage** (ADR-0013): `DocumentFolder`
  (Name, Pfad unter der Wurzel, Modul, Standard je Modul, Sichtbarkeit über
  Gruppen), `Document.FolderId/FolderPath/FileModifiedAt/MissingSince`.
  Wurzel `Souffleur:Ablage:Pfad` (im Haus `/ablage`, Freigaben per
  `deploy/haus/docker-compose.override.example.yml`); ohne Einstellung
  `data/ablage`, wird angelegt — so läuft die Entwicklung.
  `Infrastructure/Documents/FolderStorage.cs` (Pfade immer unter der Wurzel,
  übergeht `@eaDir`/`#recycle`/Versteckte/`~$`, lesbare Namen mit „ (2)“,
  Löschen in `#recycle/souffleur/<Datum>`), `FolderService.cs` (Ordner
  verwalten, Einlesen: Pfad → Prüfwert → neu, Umbenennen/Verschieben behält
  das Dokument, Fehlendes bleibt mit `MissingSince`; Ruhezeit 15 s; Kopie
  der Belege nach `Belege/<Jahr>/` im Finanz-Standardordner, immer
  eingeschränkt), `FolderScanner.cs` (alle 5 Min., `Souffleur:Ablage:TaktMinuten`).
  Upload an Produktion/Gastspiel/Aufgabe/Kontakt/Termin geht in den
  Standardordner des Moduls, Unterordner mit dem Namen des Gegenstands.
  API `/api/documents/folders` (Lesen `DocumentsRead`, alles andere
  `DocumentsManage`), Formularfeld `folder` beim Hochladen (leer/`intern`/Id),
  `?folderId=` an der Liste. Oberfläche: Einstellungen → **Ordner der
  Ablage** (`features/settings/ordner.component`, Auswähler zum Durchklicken,
  Unterordner anlegen, einlesen, Belege kopieren); in Dokumente Filter
  „Ordner“, Pfad und „fehlt“ an der Zeile, „Ablegen in“ im Bogen.

- **Dokumente: Vorschau, Filter, mehrere Dateien** (09.11.2026): Liste in voller Breite, auf schmalen
  Bildschirmen weniger Spalten. Vorschaubild je Zeile
  (`features/documents/vorschaubild.component.ts`): Bilder direkt, PDFs die
  erste Seite über pdf.js (`shared/pdf-vorschau.ts`, nachgeladen, Worker als
  Asset `/pdfjs/pdf.worker.min.mjs` aus `angular.json`, höchstens zwei
  gleichzeitig). Klick auf Bild oder Namen öffnet die **Vorschau**
  (`vorschau.component.ts`, Lightbox, Pfeiltasten blättern in der gefilterten
  Liste, Escape schließt). Dafür `GET /api/documents/{id}/file?inline=true`:
  nur PDF, Bilder, Audio, Video, Text inline (nach Endung, nicht nach
  gespeichertem Typ), außer PDF mit `CSP: sandbox`; Office bleibt Anhang.
  **Filter links** in der Aktionsleiste (`ActionsRegistry.filters`,
  `FilterGroup`): Dateityp und „verknüpft mit“ filtern in der Liste,
  Schlagwort und Ordner fragen den Server; aktive Filter als Chips über der
  Liste. „Gruppen und Zugänge“ und „Ordner der Ablage“ stehen nur noch in den
  Einstellungen. **Mehrere Dateien** auf einmal (`souffleur-drop-zone
  [multiple]`, `filesChange`; Belege bleiben bei einer): ein Bogen, jede
  Datei behält ihren Namen, nacheinander hochgeladen, Fehlschläge bleiben im
  Bogen. Zugänge: „+ Gruppe“/„+ Rolle“ an den Reitern. Offen: Zeitraum als
  Filter, Vorschau für Office (bräuchte LibreOffice im Image).
- **Einstellungen → Haus** (`features/settings/haus.component`): Name,
  Anschrift, Steuernummern, Rechtsform (Liste üblicher Formen oder frei),
  Kontenrahmen (SKR03/04/42/eigener) und Gemeinnützigkeit; zeigt, wann
  Buchungen eine Sphäre verlangen. gGmbH/gUG setzen den Haken von selbst.
  Nur mit `HouseManage` änderbar, sonst lesend. API `PUT /api/organization`
  weist leere Namen ab. Den Kontenplan selbst pflegt Einstellungen →
  Kontenplan.
- **Abwesenheiten** (ADR-0012): vierte Ansicht im Personal (`features/personnel/
  abwesenheiten.component.ts`, `core/absences.service.ts`, Backend
  `Api/Features/Personnel/AbsenceEndpoints.cs` unter
  `/api/personnel/absences`, `Infrastructure/Personnel/AbsenceService.cs`).
  `Absence` (Art, Tage, halber Tag, Vertretung, Stand beantragt/genehmigt/
  abgelehnt/zurückgezogen/gemeldet; Krankheit ohne Notiz, nur
  `CertificateReceived`), `Employment` (Vertragsstand ab Tag: Wochenstunden,
  `WorkWeekdays` als Bits, Urlaubstage), `LeaveAdjustment`, `HouseHoliday`,
  `Organization.FederalState` (Vorgabe Berlin). Rechnen ohne Datenbank in
  `Domain/Personnel/WorkCalendar.cs` (Tage, Anspruch anteilig, § 9 BUrlG,
  Stundenkonto bis gestern), Feiertage gerechnet in `PublicHolidays.cs`.
  Rechte `PersonnelApprove` (420, sieht Gründe inkl. Krankheit) und
  `PersonnelRecords` (430, Verträge) — die Migration `Abwesenheiten` gibt sie
  bestehenden Häusern einmal an Administration und Geschäftsführung (per
  SQL wie `ProduktionsrechteNachtragen`; der Start ergänzt nur den
  Administrator). Kalender liest
  `CalendarSource.Absence` (6) nur als „Abwesend: Name“; die Konfliktprüfung
  meldet Abwesenheiten (auch beantragte) beim Einteilen. Glocke
  `NotificationKind.Absence`, Sprungziel `/personal?ansicht=abwesenheiten`.
  Einstellungen → **Freie Tage** (`/einstellungen/freie-tage`): Bundesland
  (`PUT /api/organization/federal-state`, `HouseManage`) und Tage des Hauses.
  Vorführdaten: Verträge für bis zu vier Mitarbeitende, ein offener Antrag,
  ein genehmigter Urlaub, eine Krankmeldung in der Vorwoche — absichtlich
  weit weg vom Heute, weil die Browsertests dieselben Leute einteilen.
  Offen: Löschfristen für Krankmeldungen, Ausland/Dienstreisen/A1 (an
  Touring), On-/Offboarding (als Aufgabenvorlagen).

- **Kopfzeile in zwei Zeilen** (04.11.2026): oben Wortmarke, mittig
  die Suche in voller Breite (`max-w-xl`), rechts Glocke, Farbschema,
  Einstellungen, Name, Abmelden; darunter die Reiter der Module. Das Schild
  „Vorführdaten" gibt es nicht mehr -- die Wortmarke trägt bei erfundenen
  Daten ein ✱ mit Hinweis im Tooltip und ist in der Entwicklung selbst der
  Schalter.
- **Gruppen in der Entwicklung**: `DatabaseInitializer.SeedDevelopmentGroupsAsync`
  legt bei jedem Start Technik, Inspizienz, Verwaltung an (nur was fehlt,
  nur mit `SeedDevelopmentData`), Mitglieder sind die beiden
  Entwicklungskonten. Damit hat „Vorschlag aus" im Bedarf je Terminart
  etwas zu zeigen; der Bogen erklärt das Feld und verweist ohne Gruppen
  auf Zugänge → Gruppen.
- **Haus wechseln in der Entwicklung**: Klick auf die Wortmarke „souffleur"
  in der Kopfzeile öffnet die Häuser aus `data/haeuser/` (`shell/vorfuehrdaten.
  component.ts`, `core/demo-houses.service.ts`). Backend
  `Api/Features/Development/` (nur unter `IsDevelopment` eingehängt) und
  `Infrastructure/Persistence/DemoHouses.cs`: leert den Bestand des Hauses,
  lässt Konten, Rollen, Rechte, Gruppen, Einstellungen, Kataloge stehen und
  seedet aus dem Profil neu -- die Anmeldung überlebt. Dasselbe von der
  Kommandozeile: `./dev.sh --haus <name>` (baut die DB komplett neu).

- **Seitenleisten** „Aktion" (links) und „Kommunikation" (rechts,
  `shell/actions-sidebar`, `shell/chat-sidebar`, Zustand in
  `core/layout.service.ts`): angepinnt und offen stehen sie neben dem Inhalt
  (`…Docked`), sonst sind sie ein Streifen, der sich unter der Maus als
  Überlagerung aufschiebt (`…Peek`, auch angepinnt) und nach Klick, Klick
  daneben oder Escape wieder schließt. Pin und
  Aufgeklappt-Zustand am Browser (localStorage).
- **E-Mail** in der Kommunikationsleiste unter dem Chat (`shell/mail-panel.
  component.ts`, `core/mail.service.ts`, Backend `Api/Features/Mail/`,
  `Infrastructure/Mail/`): schreiben an Kontakte (Feld „An" sucht im
  Adressbuch, Enter nimmt den einen Treffer oder eine getippte Adresse),
  lesen, kurz antworten. Ein Postfach je Haus, alle Angemeldeten sehen
  dasselbe; `EmailMessage`/`EmailRecipient`, wer geschrieben hat in
  `SenderUserId`, Absender am Kontakt über `ContactId`. Versand über den
  vorhandenen `IMailSender` (`Souffleur__Mail__*`); Empfang per IMAP
  (`Souffleur__Mail__Imap__Host/Port/User/Password`, MailKit,
  `MailboxPoller` alle zwei Minuten, nur lesend, Text statt HTML). Ohne
  Mailserver: `Souffleur:Mail:LogOnly` (Entwicklung) schreibt den Versand
  nur ins Protokoll, damit der Ablauf vorfuehrbar bleibt.
- **Chat** (`shell/chat-sidebar.component.ts`, `core/chat.service.ts`,
  Backend `Api/Features/Chat/`, `Infrastructure/Conversations/`): Kanäle
  offen oder geschlossen, Direktnachrichten (`POST
  /api/chat/conversations/direct/{userId}`, Titel = die andere Person),
  Teilnehmerfenster zum Aufnehmen (einzeln oder eine ganze Gruppe, die dabei
  aufgelöst wird — wer später in die Gruppe kommt, kommt nicht nach),
  Herausnehmen, Verlassen. @-Erwähnungen mit Vorschlagsliste (Personen aus
  `/api/chat/people`, Gruppen aus `/api/groups`); Enter wählt, solange die
  Liste offen ist; die Oberfläche merkt sich, was eingefügt wurde, und
  schickt beim Senden nur, was noch im Text steht. Archivieren ist der
  Regelweg (Archiv zugeklappt am Ende der Liste, Wiederherstellen aus dem
  Menü); **Löschen nur für Kanäle**, nur für den, der angelegt hat, oder
  `HouseManage`, mit Rückfrage — Direktnachrichten und Unterhaltungen an
  einem Gegenstand bleiben. Jede Verbindung hängt zusätzlich in der
  Hub-Gruppe `user:{id}`; `conversationChanged` darüber lässt die Leiste
  neu laden (neue Direktnachricht, Aufnahme, Löschung). Alles, was eine
  Unterhaltung ändert, geht über `LoadVisibleAsync` — wer einen
  geschlossenen Kanal nicht sieht, kann ihn auch nicht füllen.
  `ChatService` holt `AuthService` über den `Injector`, weil die Anmeldung
  beim Abmelden den Chat trennt (sonst ein Kreis). Kontakte ohne Konto
  können nicht chatten; Einladen fehlt (siehe unten).
- **Sortieren per Ziehen** in allen Leisten — `shared/sortierbar.directive.ts`
  (Pointer-Events, Strg/Cmd+Pfeil). Reihenfolge am Konto über
  `UserPreference` (Schlüssel/Wert) und `core/preferences.service.ts`.
  Tabellen ziehen am Griff (`data-sortier-griff`, Icon `griff`): Wertelisten
  (früher „Listen des Hauses") und Ressourcen. Auch die Blöcke der Übersicht
  (Kennzahlen je Modul + „Buchungen und Belege") ziehen am Griff, Schlüssel
  `uebersicht`; ohne eigene Reihenfolge folgen sie den Reitern. Je Block ein
  Dreipunkt-Menü (`block-menue.component`: nach oben/unten, ausblenden),
  Ausgeblendetes unter `uebersicht:ausgeblendet`; das Zahnrad rechts oben
  listet alle Blöcke mit Haken und setzt zurück. Die Bereiche aller
  Einstellungsseiten kommen aus `features/settings/bereiche.ts`.
- **Icons** zentral in `shared/icon.component.ts`, Namen als Typ `IconName`.
- **Rechte statt Rollennamen** — `Permission` (Hunderter je Bereich),
  `RolePermission`, Claims beim Anmelden (`PermissionClaimsFactory`),
  Prüfintervall 2 Minuten, `RequirePermission(...)` in
  `Souffleur.Api/Infrastructure/PermissionPolicies.cs`. Wo vorher nur die
  Anmeldung galt, gilt sie weiter. Fünf mitgelieferte Rollen (`IsBuiltIn`):
  Schlüssel englisch und nicht umbenennbar, angezeigt deutsch.
- **Gruppen** (`Group`, `GroupMember`, `GroupRole`): bündeln Rechte, nehmen
  Aufgaben an (`TaskItem.GroupId`), schlagen beim Besetzen vor
  (`DutyRequirement.GroupId`), steuern Dokument-Sichtbarkeit. Konten ↔
  Kontakte über `Contact.UserId`. Verwaltung: Einstellungen → Zugänge.
- **Aufgaben: Beteiligte und Verknüpfungen** (04.11.2026): Neben dem einen
  Zuständigen gibt es `TaskContacts` mit `Kind` Beteiligt (arbeitet mit,
  Frist im eigenen Kalender, Glocke „hat dich an einer Aufgabe beteiligt")
  und Verknüpft (Spielstätte, Agentur — nur zum Nachschlagen). Dazu
  `CostCenterId` (Kostenstelle, nur mit Finanzmodul sichtbar), Dokumente aus
  der Ablage (anheften oder hochladen, beim Anlegen vorgemerkt und nach dem
  Speichern verknüpft) und Lesezeichen. Der Bogen trennt oben „was die
  Aufgabe ausmacht" von unten „Verknüpfungen" (`fieldset`). Die Zeile zeigt
  „+n" Beteiligte und eine Büroklammer mit Dokumenten + Lesezeichen
  (`AttachmentCount`, am Server gezählt, nicht geladen). Baustein
  `shared/mehrfachauswahl.component.ts` (Chips + Auswahlfeld) für beide
  Listen. ADR-0007 bleibt: ein Zuständiger, Beteiligte sind etwas anderes.
- **Lesezeichen** (04.11.2026; an allen Gegenständen beliebig viele URLs
  mit Stichwort, immer in neuem Fenster): `Domain/Bookmarks/Bookmark`
  (`BookmarkTarget` + `TargetId`, Zahlen wie `DocumentTarget`, dazu
  Document=8, Tour=9), `BookmarkService`, `/api/bookmarks/{target}/{id}`
  (jeder Angemeldete), nur http/https, fehlendes Schema wird ergänzt.
  Baustein `shared/lesezeichen.component.ts`: mit `targetId` spricht er mit
  dem Server, ohne (`targetId` null, Bogen im Anlegen) sammelt er in
  `ausstehend`, und die Seite legt nach dem Speichern an. Eingebaut in
  Aufgabe, Kontakt, Dokument, Tournee, Gastspiel, Produktion, Termin (Migration
  `ProduktionLesezeichen` hat `ProductionLinks` übernommen). Wer einen
  Gegenstand löscht, ruft `RemoveAllAsync` (Aufgaben tun es).
- **Dokumente** (Modul 10): Ablage, Schlagworte, Verknüpfungen, Sichtbarkeit
  „alle" oder Gruppen. Wer was sieht, nur in `DocumentService.Visible`.
- **Glocke**: `Notification`, eigener Hub `/hubs/notifications`, Auslöser in
  `Api/Features/Notifications/Ausloeser.cs`. Mail je Art aus/sofort/täglich
  (Vorgabe aus) über `NotificationMailer`; SMTP aus `Souffleur__Mail__*`,
  ohne Host nur die Glocke.
- **Touring** (Modul 11, ADR-0010, aus bei Neuinstallation): `Tour` (Klammer:
  Titel, Produktion aus Katalog `Production`, Reisende `TourMember`) →
  `TourStop` (Gastspiel: Spielstätte = Kontakt mit Rolle `Venue`, Stand
  Anfrage…Abgerechnet, An-/Abreise, Gage, `VoucherId` nur Ausgangsbeleg) →
  `TourPerformance`. Kalender liest sie als `CalendarSource.Touring`
  (Reise als Balken, Vorstellung zur Zeit, Reisende als Beteiligte);
  Dokumente kennen `DocumentTarget.TourStop`. Recht `TouringManage` (800)
  bei Administration, Geschäftsführung, Disposition. Oberfläche
  `features/touring/`, Kennzahlen `touringTiles`.
- **Suche** in der Kopfzeile (`shell/suche.component.ts`, `GET /api/search?q=`):
  findet beim Tippen über alle Module, Funde nach Bereich gebündelt, Klick
  oder Enter springt hin. Strg/Cmd+K oder „/" ins Feld. Backend
  `Api/Features/Search/`: je Bereich eine `ISuchquelle` (Liste in
  `SearchEndpoints.Quellen`, Reihenfolge = Antwort), jedes Wort muss
  vorkommen (`Suchbegriff.Eingrenzen`, LIKE auf einen Heuhaufen aus den
  Feldern). Rechte und Module wie an den eigenen Endpunkten: Finanzen nur
  mit `FinanceRead`, Dokumente über `DocumentService.Visible`, Chat nur
  Sichtbares, Konten/Gruppen nur mit `AccountsManage`; abgeschaltete Module
  fehlen. **Sprungziele** sind Abfrageparameter, die die Komponenten über
  `shared/sprungziel.ts` (`verfolgeAdresse`) auswerten: `/kontakte?kontakt=`,
  `/aufgaben?aufgabe=[&erledigte=1]`, `/kalender?tag=JJJJ-MM-TT`,
  `/dokumente?dokument=`, `/finanzen?buchung=`, `/finanzen/belege?beleg=&jahr=`,
  `/finanzen/bank?umsatz=&konto=`, `/einstellungen/ressourcen?ressource=`,
  `/uebersicht?chat=` (die Chat-Leiste hört auf den Router). Ein neues Modul
  bringt eine Quelle und ein Sprungziel mit. Noch ohne Quelle: Touring,
  Ticketing (lebt in pretix), Dienstplan/Zeiten.
- **Produktionen** (Modul 12, ADR-0011, an bei Neuinstallation; Reiter vor
  Touring -- `SouffleurModules.All` ist die Reiterreihenfolge, nicht die
  Zahlenfolge): `Production` (Titel wie im Spielplan, Untertitel, Stand in
  Planung → in Probe → im Spielplan → abgespielt, Premiere, Derniere, Budget,
  Beschreibung, Recherche, Notizen) **trägt ihre Vorgaben selbst**:
  `ProductionRole` (Rolle, Bereich aus „Funktionen am Termin“, Kontakt oder
  offen), `ProductionBudgetLine` (Positionen, Summe gegen das Budget),
  `ProductionNeed` (Art Material = Bedarf, Art Document = nötige Papiere,
  abhakbar); Links sind Lesezeichen (`BookmarkTarget.Production`, Baustein
  `souffleur-lesezeichen`; die Migration `ProduktionLesezeichen` hat die
  frühere Tabelle `ProductionLinks` hinübergetragen). Abschnittsweise
  speichern: `PUT /api/productions/{id}/{roles|budget|needs}` ersetzt genau
  seine Liste; `POST …/{id}/roles` und `DELETE …/{id}/roles/{roleId}` für den Weg
  vom Kontakt aus, `GET …/castings/{contactId}` liest, wo jemand besetzt ist.
  **Und ist die Klammer**: Aufgaben, Termine, Serien und Belege tragen
  `ProductionId` (SetNull), Dokumente das Ziel `DocumentTarget.Production`,
  Tourneen zeigen auf die Produktion (`CatalogKind` 7 ist weggefallen; die
  Migration `Produktionen` hat die Einträge mit gleicher Id übernommen, die
  Migration `ProduktionPlanung` die Beteiligten als Rollen mit dem Namen ihrer
  Funktion). `ProductionService.OverviewAsync` liest alles zusammen;
  `FiguresAsync` zählt und liefert den **Herzschlag** (`ActivityAsync`: 26
  Wochen, Aufgaben angelegt/geändert/erledigt, Termine, Belege, angeheftete
  Dokumente). Kosten = Eingangsbelege brutto, Einnahmen = Ausgangsbelege, nur
  mit `FinanceRead`. Ein festgeschriebener Beleg nimmt noch seine Produktion
  an (`AuditBehaviour.Organisational`), sonst nichts. Löschen nur ohne Anhang,
  sonst abspielen. Recht `ProductionsManage` (900) bei Administration,
  Geschäftsführung, Disposition. Oberfläche `features/produktionen/` als
  **Produktionsbuch**: Register links, eine Produktion rechts mit allen
  Abschnitten, Pfeiltasten blättern, Sprungziel `/produktionen?produktion=`,
  `?neu=1`; Herzschlag `shared/aktivitaet.component.ts` (klein Linie, groß
  Säulen). Jeder Abschnitt des Buchs hat ein **Plus**, das an Ort und Stelle
  anlegt: Rolle, Position, Bedarf, Papier (Bogen mit leerer Zeile),
  Aufgabe (Schnellfeld), Termin (der Kalenderdialog `souffleur-event-dialog`
  mit Eingabe `production`), Dokument (Datei → Ablage → Verknüpfung), Beleg
  (Datum, Richtung, Betrag, Beschreibung, Datei; Papierbeleg ohne Datei).
  Die Überschriften führen ins Modul. Auswahlfeld in Aufgabe, Termin und
  Beleg; im Kontaktbogen der Abschnitt „Produktionen“ (wo besetzt, in
  Produktion besetzen); Kennzahlen
  `productionTiles`; `/aufgaben?neu=1&produktion=` öffnet den Bogen mit
  vorbelegter Produktion. Offen: Kosten nach Kostenarten/Kostenstellen,
  Belege je Budgetposition, Verkäufe je Produktion aus dem Ticketing.

## Offen

- **Migrationen ohne Transaktion** (`AufgabenBeteiligteLesezeichen`,
  `AblageOrdner`): melden beim Start, dass ein Schritt ausserhalb einer
  Transaktion läuft (SQLite-Tabellenumbau). Ein Abbruch genau dann
  hinterliesse eine halbe Migration.
- **Ablage auf einer echten Synology nicht erprobt**: DSM-ACLs gegen den
  Container-Benutzer, Zeitstempel über SMB, Papierkorb `#recycle/souffleur/`.


Älter, ebenfalls offen:

- Fehlerprotokoll in der Datenbank mit Ansicht für die Verwaltung (braucht
  ADR: keine Nutzdaten). Litestream für die Sicherung.
- Barrierefreiheit in Bögen, Fenstern und Menüs: `barrierefreiheit.spec.ts`
  misst nur den Zustand nach dem Laden.

## Betrieb

Siehe `deploy/haus/README.md`. Das Image läuft mit
`ASPNETCORE_ENVIRONMENT=Production`.

## Fallen

- **Barrierefreiheit** (`e2e/barrierefreiheit.spec.ts`, axe-core, WCAG 2.1 AA,
  hell und dunkel): Gedimmt wird mit der Klasse `gedimmt` (Farbe `faint`), nie
  mit `opacity` -- die senkt den Kontrast unter 4,5:1. Neue Farbtöne für Text
  auf allen Flächen gegen 4,5:1 rechnen. Das Suchfeld der Kopfzeile ist eine
  `combobox`; in Tests Auswahlfelder deshalb über `getByRole('main')`
  eingrenzen. `getByLabel`/`getByRole` vergleichen Teiltext -- bei kurzen
  Namen wie „Nachricht“ oder „Anlegen“ `exact: true`.

- `Entity` vergibt die Id schon im Konstruktor. Neue Kindzeilen an einem
  **geladenen** Objekt immer ausdrücklich `context.X.Add(...)` — sonst macht
  EF ein UPDATE auf eine Zeile, die es nicht gibt.
- Angular-`effect`, der ein Signal liest und schreibt, löst sich selbst aus —
  `untracked` benutzen.
- Tailwind-Klassen mit eckigen Klammern in `[class.…]` werden nicht gebaut;
  dann Inline-Stil.
- `<a>` ist im Browser nativ ziehbar — bei sortierbaren Links
  `draggable="false"`.
- Playwright: `allInnerTexts()` wartet nicht; Texte von `<option>` nur über
  `allTextContents()`. Die Tests laufen parallel gegen eine Datenbank —
  eigene Testdaten anlegen und wegräumen, nicht die Vorführdaten verändern.
- `./dev.sh` nie gleichzeitig mit `dotnet test` starten (beide bauen
  `bin/`, das Backend stirbt still); Ausgabe in eine Datei statt durch `tail`.
- Browsertests laufen je Arbeitskopie auf eigenem Port (5100–5499, aus dem
  Pfad abgeleitet) mit eigenem Datenverzeichnis — mehrere Worktrees können
  gleichzeitig testen. Nur `./e2e-server.sh` von Hand nimmt 5081.
- Ein unbekannter `/api/…`-Pfad liefert die SPA-`index.html` mit 200 — den
  Inhalt prüfen, nicht den Status.
