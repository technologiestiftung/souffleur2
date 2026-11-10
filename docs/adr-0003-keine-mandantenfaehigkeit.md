# ADR-0003: Keine Mandantenfähigkeit

**Stand:** 29.10.2026 · **Status:** entschieden · **Gilt für:** souffleur 2

## Entscheidung

souffleur 2 wird **nicht mandantenfähig**. Jedes Haus betreibt eine eigene Instanz
mit eigenem Container, eigener Datenbank und eigenem Datenverzeichnis. Es gibt
keine Installation, in der zwei Häuser nebeneinander liegen — nicht als Ausbaustufe,
nicht als Sonderfall.

Das Haus selbst ist ein **Stammdatensatz** (`Organization`), kein Tenant. Es trägt
Name, Anschrift, Steuernummer und KSK-Betriebsnummer, weil Belege, DATEV-Export und
SEPA-Dateien das brauchen — nicht, um Daten zu trennen.

## Begründung

souffleur läuft im Haus, auf der Hardware des Hauses; einen zentralen Dienst für
mehrere Häuser gibt es nicht. Damit ist
die Datentrennung schon durch das Blech erledigt und braucht keine zweite Schicht in
der Software.

Mandantenfähigkeit nachträglich einzuziehen ist teuer. Sie vorsorglich einzubauen ist
es auch — und zwar dauerhaft, in jeder Abfrage.

## Was das konkret heißt

| | |
|---|---|
| Keine `TenantId` an Entitäten, keine globalen Abfragefilter | jede Abfrage bleibt eine Abfrage |
| `Organization` ist ein Einzeldatensatz, erzwungen per Check-Constraint | kein Zustand, in dem unklar ist, wessen Haus gemeint ist |
| Keine Rolle oberhalb des Hauses | `Administrator` ist Hausadministrator, mehr gibt es nicht |
| Sicherung ist eine Datei | ein Haus zurücksichern, ohne andere zu berühren |
| Aufbewahrungsfristen gelten je Haus | kein Löschen fremder Daten aus Versehen |

## Der Preis

**Aktualisierungen laufen pro Haus.** Zehn Häuser heißt zehn Installationen, die jemand
auf einen neuen Stand bringt. Das ist der bewusst in Kauf genommene Nachteil; der Weg
dorthin ist in ADR-0002 § 8 weiterhin offen.

Wer souffleur je als gehostete Lösung für mehrere Häuser anbieten wollte, baut dafür
nicht um, sondern startet pro Haus einen weiteren Container. Das ist die vorgesehene
Antwort auf diesen Fall.

## Umstoßkriterium

Nur eines: Ein Träger betreibt mehrere Häuser und verlangt ausdrücklich **eine**
gemeinsame Buchhaltung über alle Häuser hinweg. Dann ist neu zu entscheiden — und
zwar vor dem Umbau, nicht danach.
