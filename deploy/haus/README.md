# souffleur im Haus

Eine Maschine, ein Container, ein Datenverzeichnis. Von aussen kommt niemand
herein.

## Einrichten

1. Das Repository auf die Maschine holen (NAS, Mini-PC, Server) und in
   dieses Verzeichnis wechseln.
2. `docker compose build` — baut das Image aus dem Quellcode. Auf einem
   schwachen NAS lieber auf einem anderen Rechner bauen und mit
   `docker save souffleur:latest | ssh <nas> docker load` hinueberbringen.
3. `.env.example` nach `.env` kopieren und ausfuellen. Wichtig ist
   `SOUFFLEUR_USER`: `id -u` und `id -g` auf dieser Maschine ablesen.
4. `mkdir -p data && docker compose up -d`
5. Im Browser auf `http://localhost:8080` (auf der Maschine selbst),
   anmelden, Passwort aendern, danach die beiden `SOUFFLEUR_ADMIN_*`-Zeilen
   aus der `.env` entfernen.
6. Den Zugang einrichten -- siehe unten.

## Zugang von aussen

Die Mitarbeitenden eines Hauses arbeiten nicht nur im Haus. Der Spielplan
wird abends von zuhause geaendert, die Probe unterwegs nachgesehen.

⚠️ **Nicht einfach Port 8080 im Router freigeben.** Der Port spricht HTTP.
Anmeldungen und Buchhaltungszahlen gingen damit unverschluesselt durchs
Netz, lesbar in jedem offenen WLAN.

**Auf einer Synology** (empfohlen, alles schon vorhanden):

1. *Systemsteuerung › Externer Zugriff › DDNS* -- einen Namen einrichten
   (z. B. `<haus>.synology.me`).
2. *Systemsteuerung › Sicherheit › Zertifikat* -- Let's-Encrypt-Zertifikat
   fuer diesen Namen holen.
3. *Anmeldeportal › Erweitert › Reverse Proxy* -- eine Regel:
   von `https://<haus>.synology.me:443` auf `http://localhost:8080`.
4. Im Router **nur Port 443** auf die Synology weiterleiten, nicht 8080.

Damit laeuft der Zugriff verschluesselt, und souffleurs eigene Anmeldung
liegt dahinter. Wiederholte Fehlversuche sperren ein Konto voruebergehend.

**Ohne Synology** dasselbe mit Caddy, nginx oder Traefik auf derselben
Maschine: Zertifikat holen, auf `127.0.0.1:8080` weiterleiten, nur 443
nach aussen.

**Wenn das Haus kein Tor nach aussen oeffnen will**, geht es auch per VPN
(WireGuard laeuft auf einer Synology). Dann brauchen die Mitarbeitenden
aber auf jedem Geraet einen Client -- auch auf privaten Telefonen. Das ist
sicherer und im Alltag unbequemer; die Entscheidung gehoert dem Haus.

## Aktualisieren

Von Hand, am besten ausserhalb der Spielzeiten — ein Theater spielt abends:

```
git pull
docker compose build
docker compose up -d
```

Die Datenbank wird beim Start automatisch auf den neuen Stand gebracht.
Vorher `data/` sichern: Ein Zurueck auf eine aeltere Fassung kennt die
Datenbank nicht.

## Ordner der Synology

souffleur kann mit den Ordnern arbeiten, die das Haus schon in der File
Station hat (ADR-0013): Was dort liegt, erscheint unter Dokumente, und was in
souffleur hochgeladen wird, landet dort mit lesbarem Namen.

1. In der DSM einen Benutzer fuer souffleur anlegen (etwa `souffleur`) und
   ihm Lesen und Schreiben auf die Freigaben geben, die souffleur nutzen
   soll. Per SSH `id souffleur` ablesen und Kennung und Gruppe als
   `SOUFFLEUR_USER` in die `.env` schreiben. Dem Benutzer muss auch `data/`
   gehoeren: `chown -R <uid>:<gid> data`.
2. `docker-compose.override.example.yml` als `docker-compose.override.yml`
   kopieren und je Freigabe eine Zeile eintragen.
3. `docker compose up -d`
4. In souffleur: Einstellungen → Ordner der Ablage. Dort die Ordner
   auswaehlen und den Modulen zuordnen. Der Standardordner der Finanzen
   bekommt Kopien der Belege; die Originale bleiben in `data/`.

Ohne diese Datei legt souffleur Dokumente wie bisher nur in `data/` ab.
Die Ordner auf der Synology sichert das Haus so, wie es sie schon sichert --
souffleur aendert daran nichts.

## Was gesichert werden muss

Das Verzeichnis `data/`. Darin liegen die Datenbank und alle Belege; es ist
durch nichts zu ersetzen. Das Image laesst sich jederzeit neu bauen, die
Konfiguration steht in der `.env`.

## Was nach aussen geht

Nichts. souffleur telefoniert nicht nach Hause. Verbindungen nach aussen
entstehen nur, wenn das Haus sie selbst einrichtet: Mailversand, Postfach,
Bankabruf, Ticketing.
