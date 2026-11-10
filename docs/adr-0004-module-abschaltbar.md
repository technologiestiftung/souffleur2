# ADR-0004: Module sind je Haus abschaltbar

**Stand:** 30.10.2026 · **Status:** entschieden · **Gilt für:** souffleur 2

## Entscheidung

souffleur 2 besteht aus Modulen, die jedes Haus einzeln ein- und ausschalten kann.
Ein abgeschaltetes Modul ist **nicht nur unsichtbar, sondern nicht vorhanden**: Es
erscheint weder als Reiter noch in der Suche, und seine Schnittstellen antworten
mit `404`.

Nicht abschaltbar ist allein die Übersicht — ohne sie gäbe es keine Startseite.

## Warum

Die Häuser sind verschieden zugeschnitten, und zwar nicht nach Größe, sondern nach
Arbeitsweise:

| Haus | Ticketing | Warum |
|---|---|---|
| Theater am Kanal | an | eigener Vorverkauf über pretix, Anbindung besteht |
| Kollektiv Nord | aus | tourendes Kollektiv; die Karten verkaufen die Gastgeber |

Ein Modul, das ein Haus nie befüllt, ist schlimmer als keines: Es steht leer in der
Navigation, wirft Fragen auf und sieht aus wie ein Fehler.

Zwei Module sind in einer neuen Installation **von Haus aus aus** — Ticketing und die
artwork-Anbindung. Beide brauchen einen Zugang zu einem Fremdsystem, den es beim
Aufsetzen noch nicht gibt.

**Ergänzung 04.11.2026:** Auch **Touring** ist aus. Es braucht kein Fremdsystem, aber
eine Arbeitsweise, die nicht jedes Haus hat: Ein ortsfestes Theater reist nicht, und ein
leeres Touring neben dem Spielplan sähe aus wie ein Fehler. Die Regel lautet damit
allgemeiner: Aus ist, was ein Haus **nicht ohne Voraussetzung** befüllen kann — sei die
Voraussetzung ein Zugang oder eine Betriebsform. Siehe ADR-0010.

## Der Zuschnitt

| Modul | Enthält | Abschaltbar |
|---|---|---|
| Übersicht | Kennzahlen, was ansteht | nein |
| Finanzen | Buchen, Belege, Bank, Budget, Inventar | ja |
| Personal | Dienstplan, Zeiterfassung, KSK | ja |
| Ticketing | Verkäufe und Auslastung | ja, aus bei Neuinstallation |
| Aufgaben | Aufgaben, Chat, Dokumente | ja |
| Spielplan aus artwork | lesende Anbindung | ja, aus bei Neuinstallation |
| Touring | Tourneen und Gastspiele | ja, aus bei Neuinstallation |

Das Inventar liegt unter Finanzen, weil jeder Gegenstand an einem Anschaffungsbeleg
hängt. Die Künstlersozialabgabe liegt unter Personal, weil ihre Bemessungsgrundlage
Honorare an Personen sind, nicht Konten.

**Fachgebiete innerhalb eines Moduls sind keine eigenen Module.** Buchen, Belege und
Bank gehen mit den Finanzen an und aus. Sonst entstünden Zustände, in denen Belege
erfasst, aber nicht gebucht werden können.

## Die Sperre gehört auf den Server

Das ist der eigentliche Gehalt dieses ADR. Würde nur die Oberfläche den Reiter
ausblenden, bliebe die Schnittstelle offen — und ein Haus, das sein Ticketing
abschaltet, weil die Zahlen niemanden angehen, hätte nichts gewonnen.

Deshalb hängt jede Endpunktgruppe an ihrem Modul (`RequireModule`), und ein
abgeschaltetes Modul antwortet mit `404`, nicht mit `403`: Für dieses Haus gibt es
den Endpunkt nicht, und ein `403` würde verraten, dass es ihn gäbe.

Geprüft wird das in `tests/Souffleur.Api.Tests/ModuleGateTests.cs`, lesend **und**
schreibend.

## Preis

Jedes neue Fachmodul muss an sein Modul gebunden werden. Vergisst man es, ist der
Endpunkt offen, ohne dass es auffällt — die Oberfläche zeigt ihn ja nicht. Dagegen
hilft nur, dass es für jedes Modul einen Test gibt wie den vorhandenen.

Daten abgeschalteter Module bleiben in der Datenbank. Das ist beabsichtigt: Wer sein
Ticketing ein halbes Jahr abschaltet und wieder einschaltet, soll seine Zahlen
wiederfinden. Zum Löschen gibt es keinen Knopf, und bei buchführungsrelevanten Daten
darf es ihn nicht geben.

## Umstoßkriterium

Wenn sich zeigt, dass Häuser Teile *innerhalb* eines Moduls abschalten wollen — etwa
Finanzen ohne Inventar —, ist der Zuschnitt neu zu schneiden, nicht die Mechanik zu
verfeinern.
