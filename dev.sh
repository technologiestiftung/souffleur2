#!/usr/bin/env bash
#
# Startet souffleur lokal: Backend auf 5080, Oberfläche auf 4200.
# Beenden mit Strg-C -- beides wird zusammen gestoppt.
#
#   ./dev.sh                  eingebauter Beispielbestand (bzw. was zuletzt lief)
#   ./dev.sh --reset          Datenbank neu, eingebauter Beispielbestand
#   ./dev.sh --haus <name>    Datenbank neu aus data/haeuser/<name>.json
#   ./dev.sh --haus           zeigt die vorhandenen Häuser

set -euo pipefail
cd "$(dirname "$0")"

export ASPNETCORE_ENVIRONMENT=Development
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1

HAEUSER=data/haeuser

case "${1:-}" in
  --reset)
    echo "Datenbank wird zurückgesetzt…"
    rm -rf src/Souffleur.Api/data
    ;;
  --haus)
    # Ein bestimmtes Haus vorführen: Die Datenbank entsteht neu aus dessen
    # Profil, damit sich das Haus in Spielplan und Spielstätten wiedererkennt.
    # Die Profile sind Kundendaten und liegen deshalb nicht im Repository
    # (siehe HouseProfile.cs), sondern liegen als data/haeuser/<name>.json.
    profil="$HAEUSER/${2:-}.json"
    if [ -z "${2:-}" ] || [ ! -f "$profil" ]; then
      [ -n "${2:-}" ] && echo "Kein Profil $profil."
      echo "Vorhandene Häuser (./dev.sh --haus <name>):"
      found=0
      for f in "$HAEUSER"/*.json; do
        [ -f "$f" ] || continue
        found=1
        printf '  %-12s %s\n' "$(basename "$f" .json)" \
          "$(sed -n 's/^ *"name": *"\([^"]*\)".*/\1/p' "$f" | head -1)"
      done
      [ "$found" = 1 ] || echo "  (keine -- $HAEUSER ist leer)"
      exit 1
    fi
    echo "Datenbank wird neu aufgebaut für $(sed -n 's/^ *"name": *"\([^"]*\)".*/\1/p' "$profil" | head -1)…"
    rm -rf src/Souffleur.Api/data
    mkdir -p src/Souffleur.Api/data
    cp "$profil" src/Souffleur.Api/data/haus.json
    ;;
esac

if [ ! -d src/Souffleur.Web/node_modules ]; then
  echo "Abhängigkeiten der Oberfläche werden geholt…"
  (cd src/Souffleur.Web && npm install --silent)
fi

cleanup() { kill 0 2>/dev/null || true; }
trap cleanup EXIT INT TERM

dotnet run --project src/Souffleur.Api &

# Warten, bis das Backend antwortet -- sonst läuft die Oberfläche
# in Verbindungsfehler, bevor es steht.
for _ in $(seq 1 60); do
  if curl -sf http://localhost:5080/api/health >/dev/null 2>&1; then break; fi
  sleep 0.5
done

(cd src/Souffleur.Web && npx ng serve) &

cat <<'INFO'

  souffleur läuft.

    Oberfläche   http://localhost:4200
    API          http://localhost:5080
    OpenAPI      http://localhost:5080/openapi/v1.json

    Anmeldung    admin@localhost
                 Entwicklung1234

  Beenden mit Strg-C. Mit ./dev.sh --reset startet die Datenbank neu,
  mit ./dev.sh --haus <name> mit dem Spielplan eines anderen Hauses.

INFO

wait
