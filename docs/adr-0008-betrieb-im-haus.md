# ADR-0008: Das Haus holt sich seine Aktualisierung selbst

**Stand:** 02.11.2026 · **Status:** entschieden, in Erprobung — mit der
Veröffentlichung am 10.11.2026 abgelöst

> **Nachtrag 10.11.2026.** souffleur 2 wird nicht weiterentwickelt und ist mit
> diesem Stand veröffentlicht. Registry, CI und `update.sh` sind damit
> entfallen: Ein Haus baut das Image selbst aus dem Quellcode
> (`deploy/haus/README.md`). Die Überlegungen unten bleiben als Begründung
> stehen — wer souffleur weiterführt und wieder verteilt, findet hier den
> erprobten Weg.

## Worum es geht

souffleur läuft ab Mitte November 2026 **in den Häusern**, auf deren eigener
Maschine — nicht als Dienst der Technologiestiftung. Damit stellt sich eine
Frage, die es bei einem zentralen Dienst nicht gibt: Wie kommt eine neue
Fassung auf N Maschinen, die in fremden Netzen stehen, hinter fremden Routern,
ohne feste Adresse?

## Verworfen: ein Tunnel ins Haus

Naheliegend wäre ein VPN je Haus. Man baut einen WireGuard-Tunnel auf, und von
Berlin aus lässt sich jede Maschine erreichen wie eine im eigenen Netz.

Dagegen sprechen zwei Dinge, und das erste wiegt schwerer als es zunächst
aussieht.

**Ein dauerhafter Zugang ins Hausnetz macht den Entwickler zum Fernwarter.**
souffleur gehört dem Haus und läuft in dessen Verantwortung. Ein Zugang, über
den jederzeit jemand von aussen auf die Maschine im Haus kommt, verwischt
diese Grenze. Dazu kommt die Haftung: Der Tunnel endet nicht im Container, sondern im
Netz des Hauses.

**Eingehende Verbindungen sind je Haus eine eigene Verhandlung** — mit dessen
Anschluss, Router und, wo vorhanden, IT-Dienstleister. Bei drei Häusern ist das
machbar. Bei fünfzehn ist es ein Zweitberuf.

## Entschieden: Pull

Die Maschine im Haus holt sich die neue Fassung selbst.

```
  CI baut  →  Registry  ←  update.sh im Haus, stündlich
```

- Die CI baut das Image für amd64 und schiebt es in die Gitea-Registry.
- `deploy/haus/update.sh` läuft auf der Maschine des Hauses, prüft stündlich
  und spielt ein, wenn etwas Neues da ist.
- **Nur ausgehende Verbindungen.** Die gehen durch jede Firewall, ohne dass
  jemand etwas freischalten muss.

Das Haus bekommt kein Repository und baut nichts. Ein .NET- und Angular-Build
gehört nicht auf ein NAS, und der Quellcode gehört nicht dorthin.

## Drei Eigenheiten des Kulturbetriebs

**Wartungsfenster.** Ein Theater spielt abends. Ein Neustart mitten in der
Vorstellung ist eine Störung, auch wenn er zwanzig Sekunden dauert.
Eingespielt wird zwischen 03:00 und 05:00; außerhalb passiert nichts, auch
wenn etwas bereitläge. Wer es eilig hat, ruft `update.sh --jetzt`.

**Rollback.** Wird die neue Fassung nicht gesund, läuft die alte wieder — und
erst dann gilt der Vorgang als abgeschlossen. Dieselbe Logik wie beim Server,
dort am kaputten Stand erprobt.

**Rückmeldung.** Bei verteilten Installationen weiß sonst niemand, wer auf
welchem Stand läuft. Dafür braucht es nichts Neues: `uptime-kuma` läuft
bereits und kennt den Monitortyp *Push* — eine Adresse, die die Installation
periodisch aufruft. Bleibt der Ruf aus, schlägt Kuma von selbst Alarm.

Deshalb meldet sich `update.sh` bei **jedem** Lauf, nicht nur wenn es etwas
getan hat: Schweigen soll etwas bedeuten. Mitgeschickt wird der Ausgang und
das laufende Image — **keine Titel, keine Namen, keine Zahlen aus dem Haus**.
Ohne eingetragene Adresse geht gar nichts hinaus.

Eine unerreichbare Registry meldet sich als Hinweis, nicht als Störung: Die
Installation läuft ja, sie hat nur nicht nachsehen können.

## Die Betriebsdateien reisen im Image mit

`update.sh`, das Compose und die Anleitung liegen unter `/app/haus/` im Image.
Nach einer erfolgreichen Aktualisierung holt `update.sh` sie heraus und
erneuert sich selbst.

Ohne das müsste bei jeder Änderung am Skript jemand in jedes Haus fahren oder
Dateien verschicken. Bei verteilten Installationen ist das das eigentliche
Problem — nicht die Anwendung, sondern alles drumherum.

## Zugang von aussen

Souffleur ist keine Anwendung, die nur im Haus gebraucht wird: Der Spielplan
wird abends von zuhause geaendert, die Probenzeit unterwegs nachgesehen. Eine
Installation, die nur im Hausnetz erreichbar ist, waere an der Arbeitsweise
vorbeigebaut.

Der Weg dorthin gehoert aber nicht in souffleur, sondern davor: ein Reverse
Proxy mit Zertifikat auf derselben Maschine. Eine Synology bringt ihn mitsamt
DDNS und Let's Encrypt mit; die Anleitung in `deploy/haus/README.md`
beschreibt die vier Schritte.

Deshalb bindet die Vorlage auf **127.0.0.1**, nicht auf alle Adressen. Wer
sie oeffnen will, muss es ausdruecklich tun -- und liest dabei die Warnung,
dass dieser Port HTTP spricht. Der haeufigste Fehler bei einer
Selbstinstallation ist nicht die fehlende Verschluesselung, sondern die
versehentliche Freigabe des falschen Ports.

Ein VPN bleibt die sicherere Variante und ist dem Haus freigestellt. Als
Vorgabe taugt es nicht: Niemand im Ensemble installiert einen VPN-Client,
um nachzusehen, wann die Probe ist.

## Was offen ist

- **Die Push-Monitore in uptime-kuma anlegen.** Je Installation einer; die
  angezeigte Adresse kommt in deren `.env`.
- **Nicht jede Synology taugt.** Der Container Manager wird nur für
  x86-Modelle angeboten. Eine DS720+ geht, eine DS215+ oder DS216 nicht —
  dort braucht es eine andere Maschine. Das ist je Haus zu klären, **bevor**
  im November jemand davorsteht.
- **Die Anmeldung gehoert vor dem ersten Haus geprueft.** Sie steht dann im
  Internet, nicht mehr nur im Hausnetz. `lockoutOnFailure` ist aktiv, aber
  das ist der Anfang, nicht das Ergebnis einer Pruefung.
- **arm64 wird nicht gebaut.** Der erste Versuch lief über QEMU auf dem
  x86-Runner und blieb in `setup-qemu-action` hängen — bei Last 0,55, also
  wartend, nicht rechnend. Da alle bekannten Zielmaschinen x86 sind (DS720+,
  Mini-PC), ist das kein Verlust. Wird arm64 gebraucht, gehört es auf einen
  ARM-Runner, nicht unter Emulation.

## Die Anmeldung, bevor sie öffentlich steht

Geprüft am 02.11.2026, weil souffleur ab Mitte November nicht mehr im Hausnetz
steht, sondern über einen Reverse Proxy aus dem Internet erreichbar ist.

**Was in Ordnung war:** zwölf Zeichen Mindestlänge ohne Sonderzeichenzwang
(Länge schlägt Komplexität), eindeutige Adressen, dieselbe Antwort für
„Konto gibt es nicht" und „Passwort falsch", `HttpOnly` und `SameSite=Strict`
am Sitzungscookie, und nur zwei Endpunkte ohne Anmeldung — die Anmeldung
selbst und der Abo-Feed, dessen Adresse 24 zufällige Bytes enthält.

**Was fehlte:**

Das Cookie trug **kein `Secure`-Flag**. Hinter einem Reverse Proxy kommt die
Anfrage als HTTP an, auch wenn der Browser HTTPS gesprochen hat — die
Anwendung hielt die Verbindung für unverschlüsselt und verzichtete auf das
Flag. Der Browser hätte das Cookie anschließend auch über HTTP gesendet,
mitlesbar in jedem offenen WLAN. Jetzt wertet die Anwendung die
weitergereichten Header aus (`UseForwardedHeaders`) und setzt das Flag, außer
`Souffleur:RequireHttps` ist ausdrücklich abgeschaltet.

Die **Kontosperre war großzügig**: zehn Fehlversuche, fünf Minuten Sperre —
also 120 Versuche je Stunde und Konto. Im Hausnetz vertretbar, öffentlich
nicht. Jetzt fünf Versuche, fünfzehn Minuten.

Es gab **keine Grenze je Absender**. Die Kontosperre schützt ein Konto, nicht
vor jemandem, der Adressen durchprobiert oder den Endpunkt zumacht.

### Eine Grenze, die beinahe schlimmer gewesen wäre als die Lücke

Der erste Versuch waren zehn Anmeldungen je Minute und Absender. Die
Browsertests fielen daraufhin reihenweise um — ab dem zwölften.

Das war kein Testproblem. Ein Haus sitzt hinter **einer** öffentlichen
Adresse: Melden sich morgens zwanzig Leute an, kommen alle vom selben
Absender. Die Grenze hätte die Belegschaft ausgesperrt und nicht den
Angreifer.

Jetzt sind es fünfzig Versuche je fünf Minuten, einstellbar über
`Souffleur:LoginAttemptsPerWindow`. Gegen das Raten eines Passworts schützt
ohnehin die Kontosperre; diese Grenze soll nur die Flut abfangen.
