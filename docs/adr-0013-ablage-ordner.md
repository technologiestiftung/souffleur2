# ADR-0013: Ordner der Synology als Ablage

**Stand:** 09.11.2026 · **Status:** entschieden · **Gilt für:** souffleur 2

## Anlass

Die Häuser organisieren ihre Dateien längst in der File Station ihrer Synology:
ein Ordner für die Verwaltung, einer je Produktion, einer für Förderanträge. Das
Modul Dokumente (Modul 10) legte bisher alles in `data/documents/<Jahr>/<Guid>` ab.
Dort sieht die File Station es nicht, und die Namen sind für Menschen unlesbar. Das
Haus hatte damit zwei Ablagen, die nichts voneinander wissen.

Wolf, 05.11.2026: beliebig viele Verzeichnisse auf der Synology angeben, Modulen
zuordnen — Finanzen, Aufgaben, Dokumente, Produktion.

## Entscheidung

### 1. Der Ordner ist die Wahrheit, in beide Richtungen

Was in souffleur hochgeladen wird, landet im Ordner mit lesbarem Namen. Was jemand
in der File Station hineinlegt, erscheint in souffleur. souffleur fügt hinzu, was
die File Station nicht kennt: Schlagworte, Verknüpfungen, Sichtbarkeit, Lesezeichen.

Verworfen wurden die beiden Einbahnstraßen. „Nur souffleur → Synology“ ließe
unsichtbar, was das Haus selbst ablegt. „Nur Synology → souffleur“ zwänge zum
Hochladen in die File Station.

### 2. Eingehängt, nicht über die File-Station-API

souffleur läuft im Haus auf der Synology (ADR-0008). Die Freigaben kommen als
Volumes unter `/ablage/<Freigabe>` in den Container
(`docker-compose.override.yml`, Vorlage in `deploy/haus/`). souffleur liest und
schreibt dort als Dateisystem.

Verworfen wurde die DSM-Schnittstelle (File Station API) mit Dienstkonto. Sie
funktionierte auch, wenn souffleur woanders läuft. Dafür bräuchte sie gespeicherte
Zugangsdaten, wäre langsamer und hätte ein Vielfaches an Code. Läuft souffleur
einmal nicht auf dem NAS, ist ein SMB-Mount auf dem Wirt derselbe Weg.

Ohne Einstellung `Souffleur:Ablage:Pfad` liegt die Wurzel in `data/ablage` und wird
angelegt. So arbeiten Entwicklung und Testinstanzen ohne Synology. Ist der Pfad
eingestellt und fehlt, meldet souffleur „nicht eingehängt“ und legt nichts an.
Sonst schriebe es still in den Container statt auf das NAS. Aus demselben Grund
lassen sich Unterordner nur unterhalb einer Freigabe anlegen, nicht daneben.

### 3. Je Modul beliebig viele Ordner, ein Standard

`DocumentFolder`: Name, Pfad unterhalb der Wurzel, Modul, Standard ja/nein,
Sichtbarkeit (Gruppen). Ordner dürfen nicht ineinander liegen und nicht doppelt
vorkommen: Sonst gehörte dieselbe Datei zweien, und jedes Einlesen schöbe sie hin
und her.

Wohin ein Upload geht:

| Woher | Wohin |
|---|---|
| Ausdrücklich gewählt (Bogen „Ablegen in“) | Dieser Ordner — oder „bei souffleur“ |
| An einem Gegenstand (Produktion, Gastspiel, Aufgabe, Kontakt, Termin) | Standardordner des Moduls, Unterordner mit dem Namen des Gegenstands (`Produktionen/Hamlet/`) |
| Ohne Ziel | Standardordner des Moduls Dokumente |
| Kein Standard eingetragen | souffleurs eigene Ablage, wie bisher |

Gleiche Namen bekommen „ (2)“ angehängt, wie in der File Station. Überschrieben
wird nie.

### 4. Einlesen: erst der Pfad, dann der Prüfwert

Alle fünf Minuten (`Souffleur:Ablage:TaktMinuten`, 0 schaltet ab) und auf Knopfdruck
gleicht `FolderService` jeden Ordner ab:

- **Bekannter Pfad:** Haben sich Größe oder Änderungszeit geändert, wird der
  Prüfwert neu gerechnet. Sonst wird nichts gelesen; eine Synology mit 2 GB hat nichts
  zu verschenken.
- **Unbekannter Pfad, Inhalt passt zu einem Dokument, dessen Datei fehlt:** Die Datei
  wurde umbenannt oder verschoben, auch in einen anderen Ordner. Das Dokument zieht
  mit und behält Schlagworte und Verknüpfungen.
- **Sonst neu:** Ein Dokument entsteht mit der Sichtbarkeit des Ordners.
- **Bekannt, aber weg:** Das Dokument bleibt stehen und ist als „fehlt“ markiert
  (`MissingSince`). Kommt die Datei zurück, etwa aus dem Papierkorb, ist alles wieder
  da. Gelöscht wird nichts automatisch.

Übergangen werden die Verzeichnisse der Synology (`@eaDir`, `#recycle`,
`#snapshot`), versteckte Dateien, Office-Sperrdateien (`~$…`) und Verknüpfungen
(Schutz gegen Kreise). Ebenso Dateien, die jünger als 15 Sekunden sind: Sie werden
vielleicht gerade geschrieben. Ein eindeutiger Index auf (Ordner, Pfad) fängt den
Rest ab. Höchstens 20 000 Dateien je Ordner, zwölf Ebenen tief: Ein versehentlich
gewählter Wurzelordner soll nicht das ganze NAS durchwühlen.

Was die File Station hineinlegt, wird geführt, auch wenn souffleur die Art beim
Hochladen nicht annähme. Es geht als `application/octet-stream` und immer als
Anhang hinaus, wie jede Datei der Ablage.

### 5. Löschen in souffleur heißt: Papierkorb der Freigabe

Was die File Station löscht, landet im Papierkorb der Freigabe (`#recycle`).
souffleur verschiebt dorthin (`#recycle/souffleur/<Datum>/`), wenn es ihn gibt. Ein
Fehlklick in souffleur ist so umkehrbar wie einer in der File Station.

Einen Ordner in souffleur auszutragen löscht nur, was souffleur über die Dateien
wusste. Die Dateien selbst bleiben.

### 6. Belege: Kopie in den Finanzordner, das Original bleibt

Belege sind aufbewahrungspflichtig und unveränderlich (§ 146 Abs. 4 AO, GoBD). In
der File Station wären sie änderbar. Deshalb bleibt das Original in `data/vouchers`.
In den Standardordner der Finanzen kommt eine **Kopie** mit lesbarem Namen
(`Belege/2026/2026-00042 Druckerei Conrad – Plakate.pdf`), als Dokument am Beleg.

Die Kopie ist **immer eingeschränkt**: auf die Gruppen des Finanzordners, ohne
Gruppen auf die Verwaltung. Ein offener Ordner ist kein Grund, Rechnungen mit
Beträgen für alle im Haus sichtbar zu machen. Scheitert die Kopie, steht der Beleg
trotzdem. Einmal kopiert, wird nicht nachgezogen. Für Belege von vor dem Einrichten
gibt es „Belege in den Finanzordner kopieren“.

### 7. Wer was darf

| | Recht |
|---|---|
| Ordner sehen, filtern, beim Hochladen wählen | `DocumentsRead` |
| Ordner eintragen, ändern, austragen, durchsuchen, Unterordner anlegen, einlesen | `DocumentsManage` |
| Belege in den Finanzordner kopieren | `DocumentsManage` und `FinanceRead` |

Die Sichtbarkeit in souffleur gilt nur für souffleur. Wer in der File Station Rechte
auf die Freigabe hat, sieht dort alles. Die Rechte der Synology und die Gruppen in
souffleur sind zwei Schlösser an zwei Türen. Das Haus muss beide pflegen; souffleur
kann die DSM-Rechte weder lesen noch setzen.

## Was bewusst fehlt

| Fehlt | Warum |
|---|---|
| Sofortiges Erkennen (inotify) | Über SMB und Docker auf der Synology unzuverlässig; fünf Minuten genügen. |
| Ordnerrechte der DSM übernehmen | Nicht lesbar ohne DSM-API — siehe 2. |
| Versionen einer Datei | Die Synology hat Snapshots; souffleur merkt sich nur den aktuellen Prüfwert. |
| Belegkopie nach Korrektur nachziehen | Ein festgeschriebener Beleg ändert sich nicht; ein Entwurf behält seine Datei. |

## Was diese Entscheidung umstoßen würde

- souffleur läuft in einem Haus **nicht** auf derselben Maschine wie die Dateien,
  und ein Mount auf dem Wirt geht nicht. Dann wäre die DSM-API der Weg.
- Ein Haus legt so viele Dateien ab, dass der Durchlauf alle fünf Minuten spürbar
  wird. Dann bräuchte es Änderungsmeldungen statt Durchläufen.
