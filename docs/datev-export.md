# DATEV-Export: Stand, Annahmen und was das Steuerbüro prüfen muss

**Stand:** 30.10.2026 · **Betrifft:** `Souffleur.Infrastructure/Accounting/Datev`

souffleur übergibt dem Steuerbüro einen Buchungsstapel im Format **EXTF**, Version 700,
Formatname `Buchungsstapel`, Formatversion 12. Eine Datei je Zeitraum, üblicherweise
je Monat.

> ⚠️ **Noch nicht beim Steuerbüro erprobt.** Die Feldbelegung stammt aus öffentlich
> dokumentierten Umsetzungen des Formats, nicht aus der Spezifikation von DATEV.
> Bevor ein Haus damit arbeitet, muss ein Testexport eingelesen werden. Das ist
> offener Punkt 5 in ADR-0002.

---

## Was der Export tut

Er ist **kein Lesevorgang**. Mit der Übergabe werden die enthaltenen Buchungen und
ihre Belege festgeschrieben: Ab dann sind sie unveränderlich, und Korrekturen laufen
als Storno im Folgemonat. Das ist die Zeitgerechtheit und Festschreibung der GoBD,
und es ist der Grund, warum jeder Export protokolliert wird — mit Zeitraum, Anzahl,
Summe und Prüfwert der Datei.

Vor dem Export gibt es eine Vorschau, die nichts verändert und sagt, was dem Export
im Weg steht.

Ein Zeitraum geht nur einmal. Überschneidet sich eine Anforderung mit einer früheren
Übergabe, bricht souffleur ab — sonst bekäme das Steuerbüro Buchungen doppelt.

---

## Was eingestellt sein muss

Drei Angaben kommen vom Steuerbüro und lassen sich nicht erraten:

| Einstellung | Bedeutung |
|---|---|
| `Souffleur:Datev:ConsultantNumber` | Beraternummer, 1001 bis 9999999 |
| `Souffleur:Datev:ClientNumber` | Mandantennummer des Hauses, 1 bis 99999 |
| `Souffleur:Datev:AccountLength` | Länge der Sachkontonummern, üblich 4 |

Fehlt eine davon, verweigert souffleur den Export mit Klartext statt eine Datei zu
erzeugen, die DATEV ablehnt.

---

## Drei Entscheidungen, die zu prüfen sind

### 1. Windows-1252 statt UTF-8

DATEV liest den Stapel als ANSI. In UTF-8 würde aus „Bühnenbild" beim Steuerbüro
„BÃ¼hnenbild". Ein Test prüft das byteweise.

### 2. Ein Satz je Buchung, nicht je Zeile

DATEV kennt Konto und Gegenkonto in einem Satz. Bei einer Buchung mit zwei Zeilen
wird deshalb **ein** Satz erzeugt, nicht zwei — sonst stünde jeder Betrag doppelt im
Stapel und die Summe wäre verdoppelt.

**Offen:** Wie Splitbuchungen mit mehr als zwei Zeilen zu übergeben sind. Derzeit
entsteht je Zeile ein Satz ohne Gegenkonto; ob das Steuerbüro das so einlesen kann,
ist ungeprüft.

### 3. Die steuerliche Sphäre geht als Beleginfo mit — betrifft derzeit niemanden

Das Format hat kein Feld für die Sphäre eines gemeinnützigen Hauses. Sie wird
deshalb in „Beleginfo - Art 1 / Inhalt 1" mitgegeben, damit sie nicht verlorengeht.

**Ruht seit 30.10.2026** — aber nur vorläufig. Unter den betreuten Häusern ist kein
Verein; gemeinnützige GmbHs sind dagegen wahrscheinlich dabei, und die trennen
genauso. Solange kein Haus das Kennzeichen trägt, greift die Sphärentrennung nicht: Sie ist an `IsNonProfit` und den
SKR42 gebunden und bleibt aus, solange niemand eines von beidem einschaltet. Der
Code bleibt stehen, weil er nichts kostet, solange er ruht — und weil ein späterer
Neubau teurer wäre als das Liegenlassen.

Wenn er wieder gebraucht wird, ist das hier die offene Frage: Üblich ist beim SKR42,
die Sphäre über Kostenstellen abzubilden — dann konkurriert sie mit KOST1 und KOST2,
die bei uns Kostenstelle und Produktion tragen.

> ⚠️ Nicht nur Vereine sind gemeinnützig. Eine **gGmbH** trennt genauso nach den vier
> Sphären, und bei senatsgeförderten Häusern ist diese Rechtsform verbreitet. Bevor
> ein Haus produktiv bucht, gehört seine Rechtsform und sein Gemeinnützigkeitsstatus
> in die Stammdaten — sonst ist der erste DATEV-Stapel falsch.

---

## Fragen an das Steuerbüro

Vor dem ersten echten Export zu klären:

- [ ] Beraternummer, Mandantennummer, Sachkontenlänge
- [ ] Kontenliste des Hauses zum Einlesen, und welcher Rahmen gilt
- [ ] Ist das Haus gemeinnützig? Wenn ja, wohin gehört die steuerliche Sphäre —
      Beleginfo, Kostenstelle oder eigene Konten? (Sonst entfällt die Frage)
- [ ] KOST1 und KOST2: Kostenstelle und Produktion, oder anders belegt?
- [ ] Wie sollen Splitbuchungen mit mehr als zwei Zeilen aussehen?
- [ ] Belegbilder: mitliefern oder nur die Buchungen?
- [ ] Erwartet das Büro Debitoren und Kreditoren als Stammdatensatz?

---

## Was geprüft ist

In `tests/Souffleur.Infrastructure.Tests/DatevExportTests.cs`, Feld für Feld: Kopfzeile
mit Berater, Mandant, Wirtschaftsjahr und Zeitraum · Kontenrahmen im Kopf · Umlaute
als ANSI, mit Gegenprobe · Positionen von Umsatz, Soll-Haben, Konto, Gegenkonto,
Belegdatum, Belegfeld und Festschreibung · Sphäre als Beleginfo · kein Betrag doppelt ·
Festschreibung durch den Export · Protokolleintrag · Ablehnung bei Überschneidung,
leerem Zeitraum, Jahreswechsel und fehlender Beraternummer · Vorschau ohne Wirkung.

Was **nicht** geprüft ist: ob DATEV die Datei annimmt. Das kann nur das Steuerbüro.
