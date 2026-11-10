#!/usr/bin/env bash
#
# Startet souffleur für die Browsertests -- eigene Datenbank, eigener Port.
#
# Es gibt dieses Skript, weil die Tests Nachrichten schreiben und Kontakte
# anlegen. Liefen sie gegen die Entwicklungsumgebung, bliebe nach jedem Lauf
# mehr Müll in der Datenbank, in der gearbeitet wird.
#
# Getestet wird die ausgelieferte Form: ein Prozess, der die Oberfläche aus
# wwwroot mitliefert -- so wie im Container.
#
# Port und Datenverzeichnis gibt die playwright.config.ts vor, je
# Arbeitskopie verschieden -- sonst räumen sich zwei Sitzungen in zwei
# Worktrees gegenseitig die Datenbank weg. Von Hand gestartet gelten 5081
# und ein festes Verzeichnis.

set -euo pipefail
cd "$(dirname "$0")/../.."

export ASPNETCORE_ENVIRONMENT=Development
export ASPNETCORE_URLS="http://localhost:${SOUFFLEUR_E2E_PORT:-5081}"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1
# Jeder Test meldet sich an, alle vom selben Absender. Die Grenze, die im
# Betrieb eine Flut abfangen soll, wuerde hier den Testlauf abwuergen.
export Souffleur__LoginAttemptsPerWindow=10000
# Fester Name und Aufräumen beim Start, nicht beim Ende: Playwright beendet
# den Server hart, und `exec` unten ersetzt die Shell samt trap. So bleibt
# höchstens ein Verzeichnis liegen, nie mehr -- und der nächste Lauf beginnt
# trotzdem mit einer leeren Datenbank.
export Souffleur__DataDirectory="${SOUFFLEUR_E2E_DATA:-${TMPDIR:-/tmp}/souffleur-e2e}"
export Souffleur__SeedDevelopmentData=true

rm -rf "$Souffleur__DataDirectory"

(cd src/Souffleur.Web && npx ng build --configuration development >/dev/null)
exec dotnet run --project src/Souffleur.Api --no-launch-profile
