# Ticketing: Anbindung an pretix

**Stand:** 02.11.2026 · **Gilt für:** souffleur 2

Das Modul Ticketing zeigt Verkäufe und Auslastung aus dem Vorverkauf des Hauses.
Angebunden ist pretix — selbst betrieben oder gehostet auf pretix.eu.

## Was ein Haus einstellt

Zwei Schalter, bewusst getrennt:

1. **Modul an oder aus** (`Einstellungen → Module des Hauses`). Aus heißt: kein
   Reiter, keine Kennzahlen, und `/api/ticketing/*` antwortet mit `404`
   (ADR-0004). Für ein tourendes Kollektiv, dessen Karten die Gastgeber
   verkaufen, ist das der richtige Zustand.
2. **Vorverkauf angebunden oder nicht** (`Ticketing → Vorverkauf einrichten`).
   Ein Haus kann das Modul anhaben und trotzdem „Kein Vorverkauf" wählen — etwa
   solange der Zugang noch fehlt. Dann bleibt die Seite leer, und das ist kein
   Fehler.

Zum Einrichten gehören Adresse der Instanz, Veranstalter-Kurzname und ein Token.

## Das Token

**Lesen genügt.** souffleur schreibt nichts in den Vorverkauf zurück; Karten
verkauft weiterhin pretix. In pretix legt der Inhaber des Ticketing-Kontos das
Token im Team an.

Das Token steht im Klartext in der Datenbank des Hauses und verlässt den Server
nie wieder: Die API meldet nur, *ob* eines hinterlegt ist. Wer die Einstellungen
erneut öffnet, lässt das Feld leer, um das vorhandene zu behalten.

Die Datei `souffleur.db` gehört damit geschützt wie die Buchhaltung, die
ohnehin darin liegt — siehe ADR-0008.

## Wie die Zahlen zustande kommen

Übernommen aus dem alten souffleur (`src/lib/server/pretix.ts`), wo die Regeln an
den Daten eines Hauses entstanden sind. Die Rechnung steckt in
`PretixCapacity`; sie ist der einzige Teil mit Tests
(`tests/Souffleur.Infrastructure.Tests/PretixCapacityTests.cs`).

**Eine Vorstellung, nicht ein Event.** pretix kennt zwei Bauformen: Ein Event ist
entweder selbst eine Vorstellung, oder es ist eine Terminserie, und die
Vorstellungen stecken als Subevents darin. Theater legen Wiederaufnahmen als
Serie an — bei einem der Häuser waren alle 14 Produktionen Serien. Ohne diese
Unterscheidung erschiene eine ganze Spielserie als eine Zeile, mit dem Datum des
ersten Termins und der Summe aller Karten.

**Verkaufte Karten** sind gezählte Bestellpositionen: bezahlt (`p`) und
verbindlich bestellt (`n`), ohne Stornos. Bewusst *nicht* „Kapazität minus
frei": `available_number` sinkt in pretix auch durch Warenkorb-Reservierungen,
Sperrungen und Gutscheinkontingente — die Auslastung fiele dadurch systematisch
zu hoch aus. Als **Erlös** zählen nur bezahlte Positionen.

**Die Platzzahl** kommt aus dem Saalplan, wenn es einen gibt: Dessen nummerierte
Plätze sind die exakte Kapazität, gesperrte (Technik, Sichtbehinderung) zählen
nicht mit. Ohne Saalplan entscheiden die Kontingente:

| Lage | Ergebnis |
|---|---|
| lauter überschneidungsfreie Kontingente | Summe der Größen — der Regelfall |
| ein Kontingent umfasst alle anderen | dessen Größe ist die bindende Grenze |
| echte Teilüberlappung | keine Zahl, dafür ein Hinweis |
| mindestens ein unbegrenztes Kontingent | keine Zahl, dafür ein Hinweis |

Der dritte Fall ist der Grund für die ganze Fallunterscheidung: „Saalkapazität
200" und darunter „Ermäßigt 30" für dieselben Tickets ergäben addiert 230 — der
Saal hat aber 200 Plätze. Wo sich keine eindeutige Kapazität ergibt, steht keine
Auslastung, sondern der Grund.

## Betrieb

Jeder Seitenaufruf löst mehrere Abfragen je Vorstellung aus, und eine gehostete
Instanz hat Abfragegrenzen. Die Antwort wird deshalb zwischengespeichert
(voreingestellt 60 Sekunden, einstellbar bis eine Stunde). Höchstens vier
Abfragen laufen gleichzeitig; wiederholt wird nur, was sich durch Wiederholen
beheben lässt — ein `401` oder `404` wird sofort durchgereicht und erscheint als
Satz in der Oberfläche.

## Stand der Einrichtung

Erprobt wurde gegen eine selbst betriebene pretix-Testinstanz mit
Vorführdaten. Häuser, die pretix hosted (pretix.eu) nutzen, tragen dort nur
Adresse, Veranstalter und einen Token ihres Kontos ein; alles andere bleibt
gleich.
