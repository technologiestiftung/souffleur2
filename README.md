![](https://img.shields.io/badge/Built%20with%20%E2%9D%A4%EF%B8%8F-at%20Technologiestiftung%20Berlin-blue)

# souffleur 2

Verwaltungssoftware für freie Theater und kleine Kulturhäuser. Ein Haus plant
darin Produktionen und Termine, teilt Dienste ein, verwaltet Abwesenheiten,
Kontakte, Aufgaben und Dokumente, bucht mit Belegen und Bankanbindung, verfolgt
den Kartenverkauf über pretix und spricht im Chat miteinander. Module lassen
sich je Haus abschalten.

Jedes Haus betreibt seine **eigene Installation auf der eigenen Hardware**,
etwa einer Synology oder einem Mini-PC. Es gibt keinen zentralen Dienst.

> **Status:** im Wesentlichen abgeschlossen. souffleur 2 entstand 2026 bei der
> Technologiestiftung Berlin (kulturBdigital). Neue Funktionen sind nicht
> geplant; Abhängigkeiten werden über Renovate aktuell gehalten. Der Code steht
> unter MIT-Lizenz zur freien Weiterführung.

## Stack

| Schicht | Gewählt |
|---|---|
| Backend | .NET 10 LTS, ASP.NET Core Minimal APIs |
| Datenhaltung | EF Core 10 auf SQLite (WAL, `synchronous=FULL`) |
| Anmeldung | ASP.NET Identity, Cookie |
| Oberfläche | Angular 20, Tailwind 4 |
| Auslieferung | ein Container, ein Datenverzeichnis |

Es gibt **kein Mandantenmodell**: pro Haus läuft ein eigener Container. Das Haus
selbst ist ein Stammdatensatz, kein Tenant.

## Aufbau

```
src/
  Souffleur.Domain/          Entitäten und Regeln, ohne Infrastruktur
  Souffleur.Infrastructure/  EF Core, Identity, Zugänge zu Fremdsystemen
  Souffleur.Api/             Endpunkte, SignalR, liefert die Oberfläche aus
  Souffleur.Web/             Angular
tests/
  Souffleur.Domain.Tests/
  Souffleur.Infrastructure.Tests/
  Souffleur.Api.Tests/
```

## Entwickeln

Voraussetzungen: .NET SDK 10, Node 24.

```bash
# Backend auf http://localhost:5080
dotnet run --project src/Souffleur.Api

# Oberfläche auf http://localhost:4200, /api wird auf 5080 weitergereicht
cd src/Souffleur.Web && npm install && npm start
```

Beim ersten Start legt die Anwendung die Datenbank unter `data/souffleur.db` an,
erzeugt die Rollen und — sofern konfiguriert — ein Administratorkonto. Für die
Entwicklung steht das in `appsettings.Development.json`.

```bash
dotnet test                                    # Server
cd src/Souffleur.Web && npx playwright test    # Browser
```

Die Browsertests starten ihre **eigene** Instanz mit eigener Datenbank auf
einem Port, der aus dem Pfad der Arbeitskopie abgeleitet wird, und beenden
sie wieder. Sie schreiben Nachrichten und legen Kontakte
an; das gehört nicht in die Umgebung, in der gearbeitet wird. Getestet wird
dabei die ausgelieferte Form — ein Prozess, der die Oberfläche selbst
mitliefert, so wie im Container.

Es gibt sie, weil ein Startfehler in Angular nur eine weiße Seite erzeugt: Der
Server antwortet mit HTTP 200, die Datei ist vollständig, und trotzdem sieht
niemand etwas. Zwei echte Fehler sind ihnen schon aufgefallen.

Nach einer Änderung am Datenmodell:

```bash
dotnet ef migrations add <Name> \
  --project src/Souffleur.Infrastructure \
  --startup-project src/Souffleur.Api \
  --output-dir Persistence/Migrations
```

Migrationen werden beim Start automatisch angewandt.

## Betrieb

Für die Installation in einem Haus — mit Reverse Proxy, Zugang von aussen,
Ordnern der Synology und Sicherung — steht alles in
[`deploy/haus/README.md`](deploy/haus/README.md). Zum Ausprobieren genügt:

```bash
docker compose up -d --build
```

Das Administratorkonto wird nur angelegt, wenn noch kein Konto existiert. Dafür
beim allerersten Start setzen und danach wieder entfernen:

```bash
SOUFFLEUR_ADMIN_EMAIL=… SOUFFLEUR_ADMIN_PASSWORD=… docker compose up -d
```

## Entscheidungen

Die Architekturentscheidungen stehen als ADR unter [`docs/`](docs/), samt
verworfener Alternativen: kein Mandantenmodell (ADR-0003), abschaltbare Module
(ADR-0004), Kalender (ADR-0006), Betrieb im Haus (ADR-0008), Personal und
Abwesenheiten (ADR-0009, ADR-0012), Ordner der Synology als Ablage (ADR-0013).

## Zwei Dinge, die nicht verhandelbar sind

**Unveränderbarkeit.** Sobald souffleur bucht, gilt die GoBD. Festgeschriebene
Datensätze lassen sich nicht mehr ändern oder löschen; korrigiert wird per Storno.
Durchgesetzt wird das in der Anwendung (`AuditBehaviour`), nicht in SQLite — die
Datenbank kennt keine solche Sperre. Die Tests dazu stehen in
`tests/Souffleur.Infrastructure.Tests/ImmutabilityTests.cs`.

**Lizenzen.** souffleur soll vollständig unter MIT weitergegeben werden können.
Vor jeder neuen Abhängigkeit die Lizenz prüfen — ausgeliefert wird nur, was das
zulässt.

Eine Ausnahme ist bewusst getroffen: **FluentAssertions** steht ab Version 8 unter
der Xceed Community License, die kommerzielle Nutzung kostenpflichtig macht und
Open-Source-Projekte ausnimmt. Die Bibliothek ist eine reine Test-Abhängigkeit und wird
nie ausgeliefert. Sollte das je stören, ist
`AwesomeAssertions` (Apache-2.0, Fork von Version 7) der Ersatz mit identischer
Syntax.

## Mitwirken

Gepflegt wird nur noch das Nötigste: Abhängigkeits-Updates über Renovate und
gelegentliche Aufräumarbeiten. Issues und Pull Requests werden gelesen, eine
Bearbeitung ist aber nicht zugesagt. Wer souffleur weiterentwickeln will, ist
eingeladen, das Repository zu forken.

## Lizenz

Code unter MIT, siehe [`LICENSE`](LICENSE).

## Credits

<table>
  <tr>
    <td>
      Ein Projekt der <a href="https://www.technologiestiftung-berlin.de/">
        <br />
        <br />
        <img width="150" src="https://logos.citylab-berlin.org/logo-technologiestiftung-berlin-de.svg" alt="Link zur Website der Technologiestiftung Berlin" />
      </a>
    </td>
  </tr>
</table>
