# syntax=docker/dockerfile:1
#
# Ein Image, ein Container, ein Datenverzeichnis. Mehrarchitektur, damit dasselbe
# Image auf einer x86-Synology und auf einem Raspberry Pi laeuft.

# --- Oberflaeche bauen ---------------------------------------------------------
FROM --platform=$BUILDPLATFORM node:24-alpine AS web
WORKDIR /build
COPY src/Souffleur.Web/package*.json ./
RUN npm ci
COPY src/Souffleur.Web/ ./
RUN npm run build -- --output-path=/build/dist

# --- Backend bauen -------------------------------------------------------------
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS api
ARG TARGETARCH
WORKDIR /build
COPY Directory.Build.props Souffleur.slnx ./
COPY src/ src/
COPY tests/ tests/
RUN dotnet restore src/Souffleur.Api -a $TARGETARCH
RUN dotnet publish src/Souffleur.Api -c Release -a $TARGETARCH --no-restore -o /app

# --- Laufzeit ------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine
WORKDIR /app

# Zeitzonen. Alpine bringt keine mit, und souffleur rechnet ueberall damit:
# Ein Termin wird in Ortszeit eingegeben, als Zeitpunkt gespeichert und im
# iCal-Feed mit VTIMEZONE wieder ausgegeben. Ohne tzdata wirft
# TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin") -- und zwar erst zur
# Laufzeit, beim ersten Termin, nicht beim Start.
RUN apk add --no-cache tzdata
# Dasselbe Muster bei den Sprachdaten: Das Alpine-Image schaltet .NET per
# DOTNET_SYSTEM_GLOBALIZATION_INVARIANT in den Invariant-Modus und bringt
# kein ICU mit. Dann wirft new CultureInfo("de-DE") -- in der Personenliste
# des Chats, im DATEV-Export, in Suche und Glocke --, und wieder erst zur
# Laufzeit. InvariantGlobalization=false im Projekt allein hilft nicht.
RUN apk add --no-cache icu-libs icu-data-full

# Nicht als root. Das Datenverzeichnis gehoert demselben Benutzer, sonst scheitert
# SQLite beim Anlegen der WAL-Dateien.
RUN addgroup -g 10001 souffleur \
 && adduser -u 10001 -G souffleur -D -h /app souffleur \
 && mkdir -p /data && chown souffleur:souffleur /data

COPY --from=api --chown=souffleur:souffleur /app ./
COPY --from=web --chown=souffleur:souffleur /build/dist/browser ./wwwroot/

USER souffleur
VOLUME /data
EXPOSE 8080

ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false \
    ASPNETCORE_ENVIRONMENT=Production \
    Souffleur__DataDirectory=/data \
    DOTNET_gcServer=0

HEALTHCHECK --interval=30s --timeout=5s --start-period=40s --retries=3 \
  CMD wget -qO- http://127.0.0.1:8080/api/health || exit 1

ENTRYPOINT ["dotnet", "Souffleur.Api.dll"]
