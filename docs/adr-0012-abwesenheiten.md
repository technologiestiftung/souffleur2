# ADR-0012: Abwesenheiten — Urlaub, Krankheit und die Konten dahinter

**Stand:** 04.11.2026 · **Status:** entschieden · **Gilt für:** souffleur 2 ·
**Löst ab:** in ADR-0009 die Zeilen „Urlaubskonten und Krankmeldungen“ und
„Überstundensalden über Monate“ unter „Was bewusst fehlt“

## Anlass

ADR-0009 hat Urlaubskonten und Krankmeldungen zurückgestellt: „erst, wenn ein Haus
danach fragt“. Ein Haus hat gefragt, und zwar mit einer Konzeptstudie mit
Testdaten. Die Studie deckt Zeiterfassung,
Anträge mit Genehmigung, Krankmeldungen, Elternzeit, Arbeiten im Ausland und
Dienstreisen mit A1-Bescheinigung sowie On- und Offboarding ab.

Dieses ADR nimmt den ersten Teil auf: **Abwesenheiten, ihre Genehmigung und die
Konten, die daraus folgen.** Ausland, Dienstreisen und On-/Offboarding folgen
später (siehe unten).

## Entscheidung

Das Modul Personal bekommt eine vierte Ansicht, **Abwesenheiten**, neben Diensten,
Zeiten und Künstlersozialabgabe. Neue Datensätze:

| Gegenstand | Was er trägt |
|---|---|
| `Absence` | Person (Kontakt), Art, Zeitraum in ganzen Tagen, halber Tag, Vertretung, Notiz, Stand, Entscheidung mit Grund, AU-Bescheinigung liegt vor, an die Lohnbuchhaltung gemeldet am |
| `Employment` | Vertragsstand ab einem Tag: Wochenstunden, Arbeitstage (Mo–So einzeln), Urlaubstage im Jahr, Ende |
| `LeaveAdjustment` | Gutschrift oder Abzug am Urlaubskonto eines Jahres, mit Grund |
| `HouseHoliday` | Ein Tag, den das Haus jedes Jahr allen freigibt (Heiligabend) |
| `Organization.FederalState` | Bundesland, aus dem die gesetzlichen Feiertage folgen |

Sieben Festlegungen tragen das, und jede hätte auch anders ausfallen können.

### 1. Krankheit wird gemeldet, nicht beantragt — und ohne Notiz

Niemand genehmigt ein Fieber. Krank und Kind krank gelten sofort (Stand
„Gemeldet“); alles andere wartet auf eine Entscheidung.

Krankmeldungen sind **Gesundheitsdaten nach Art. 9 DSGVO**. Daraus folgt dreierlei:

- Gespeichert wird nur, *dass* und *wie lange* — und ob die Bescheinigung vorliegt.
  Kein Arzt, kein Befund. Eine Notiz nimmt der Server bei Krankheit nicht an, auch
  wenn eine kommt: Was nicht gespeichert wird, kann nicht in falsche Hände geraten.
- Den Grund einer Abwesenheit sehen nur die Person selbst und wer Anträge
  entscheidet (eigenes Recht, siehe 4).
- Im Kalender, den das ganze Haus sieht, steht nur **„Abwesend: Name“** — nie
  „krank“ oder „Urlaub“.

### 2. Ganze Tage, gezählt in den eigenen Arbeitstagen

Eine Abwesenheit hat Tage, keine Uhrzeiten. Wer nachmittags zum Arzt geht, stempelt
früher aus. Einen halben Tag gibt es nur, wenn Anfang und Ende auf denselben Tag fallen.

Arbeitstage stehen im Vertrag als einzelne Wochentage und nicht als Anzahl. Die
Abendspielleitung arbeitet Mittwoch bis Sonntag, und eine Woche Urlaub von Montag bis
Freitag kostet sie drei Tage, nicht fünf. Feiertage des Bundeslands und die Tage des
Hauses kosten nichts.

Die Feiertage werden **gerechnet** (Osterformel und Landesregeln), nicht aus einer
Tabelle geladen. So stimmt das nächste Jahr ohne Update. Einmalige Feiertage
(Berlin 2020 und 2025, 8. Mai) stehen ausdrücklich drin. Regional begrenzte fehlen,
weil souffleur die Gemeinde nicht kennt; dafür gibt es die Tage des Hauses.

### 3. souffleur rechnet die Konten, entscheidet aber nicht

Wie bei der Künstlersozialabgabe (ADR-0009, 4):

- **Urlaubsanspruch** = Urlaubstage des Vertrags, anteilig nach Kalendertagen, wenn
  der Stand nicht das ganze Jahr gilt; aufgerundet auf halbe Tage. Ob ein Haus nach
  Monaten zwölftelt (§ 5 BUrlG) oder Resturlaub verfallen lässt, regeln Vertrag und
  Haus. Dafür gibt es Gutschriften und Abzüge mit Grund statt einer veränderten Zahl:
  Am Jahresende will jemand wissen, warum es 32 Tage waren.
- **Krank im Urlaub** (§ 9 BUrlG): Tage, für die eine Bescheinigung vorliegt, gehen an
  den Urlaub zurück. Ohne Bescheinigung bleibt es Urlaub. Das Haus sieht das und kann
  nachfragen.
- **Stundenkonto** = gearbeitet + gutgeschrieben − Soll, **bis gestern**. Wer mittags
  nachsieht, soll nicht mit einem halben Tagessoll im Minus stehen. Das Soll eines
  Arbeitstags ist Wochenstunden durch Anzahl der Arbeitstage. Jede geltende
  Abwesenheit schreibt das Tagessoll gut, mit einer Ausnahme: **Freizeitausgleich**
  ist genau dafür da, das Konto abzubauen.
- **Tage ohne Erfassung**: Arbeitstage ohne Zeit und ohne Abwesenheit werden gezählt
  und angezeigt. Das ist ein Hinweis, kein Fehler.

Was eine Art für die Lohnabrechnung bedeutet (Entgeltfortzahlung, Kinderkrankengeld,
ruhendes Arbeitsverhältnis), entscheidet die Lohnbuchhaltung. souffleur führt eine
Liste dessen, wovon sie noch nicht weiß (Krankheit, Kind krank, Elternzeit,
unbezahlter Urlaub), und merkt sich, *dass* jemand Bescheid gegeben hat. Es schickt
nichts selbst.

### 4. Zwei neue Rechte, getrennt

| Recht | Nummer | Wofür |
|---|---|---|
| `PersonnelApprove` | 420 | Anträge entscheiden, alle Abwesenheiten mit Grund sehen — auch Krankmeldungen —, für andere melden, Liste für die Lohnbuchhaltung |
| `PersonnelRecords` | 430 | Vertragsdaten, Gutschriften und Tage des Hauses pflegen; Konten aller sehen |

Getrennt, weil Vertragsdaten zu pflegen nicht heißt, Krankmeldungen sehen zu dürfen.
Mitgeliefert bekommen beide Rechte Administration und Geschäftsführung. Die
Disposition bekommt keins von beiden: Sie sieht im Kalender und beim Einteilen, *dass*
jemand fehlt, und mehr braucht sie dafür nicht.

Bestehende Häuser bekommen die beiden Rechte **einmal per Migration** an diesen beiden
Rollen. Der Start ergänzt neue Rechte nur beim Administrator. Bei den anderen Rollen
kommt, was ein Haus weggenommen hat, nicht zurück (`DatabaseInitializer`). Der
Nachtrag folgt demselben Muster wie `ProduktionsrechteNachtragen`.

**Über den eigenen Antrag entscheidet jemand anderes**, außer es gibt niemanden sonst
mit dem Recht. In einem Haus mit einer Leitung wäre der Antrag sonst nicht zu
bescheiden. Die Konzeptstudie band die Freigabe an eine namentlich genannte Person.
Hier ist es ein Recht, damit die Vertretung im Urlaub der Leitung nicht am Quelltext
hängt.

### 5. Ablehnen und Aufheben nur mit Grund

Eine Ablehnung ohne Grund ist eine, über die man streitet. Dasselbe gilt für das
Aufheben eines schon geltenden Eintrags, denn Kalender und Urlaubskonto ändern sich
dadurch. Ein offener Antrag lässt sich ohne Grund zurückziehen. Ein genehmigter
Urlaub, der schon begonnen hat, lässt sich nur über die Leitung aufheben. Nichts wird
gelöscht: Abgelehnt und Zurückgezogen bleiben stehen.

Zwei aktive Einträge derselben Person dürfen sich nicht überschneiden. Die Ausnahme
ist Krankheit über einer anderen Abwesenheit, siehe 3.

### 6. Abwesenheit erscheint als Hinweis beim Einteilen, nicht als Sperre

Die Konfliktprüfung des Kalenders (Räume und Personen) kennt jetzt auch
Abwesenheiten — genehmigte, gemeldete und **beantragte**. Ein beantragter Urlaub ist
noch keiner, aber wer einteilt, soll davon wissen, bevor er den Abend verspricht.
Die Meldung lässt sich übergehen, wie jede Doppelbelegung. Auch hier steht nur
„Abwesend“ oder „Abwesenheit beantragt“, ohne Grund.

### 7. Ein Verlauf für Vertragsdaten, ohne Gehalt

`Employment` gilt ab einem Tag bis zum nächsten Stand oder bis zum Ende. Wer in der
Elternzeit auf zwanzig Stunden geht, hat im Januar ein anderes Soll als im Juni. Das
Stundenkonto des Januars soll sich dadurch nicht nachträglich ändern.

**Kein Gehalt, keine Steuerklasse, keine Sozialversicherungsnummer.** Die gehören in
die Lohnabrechnung, und die macht das Steuerbüro (ADR-0002).

## Wer was sieht

| | Eigene Abwesenheiten und Konten | Fremde mit Grund | Kalender „Abwesend“ | Konten aller | Vertragsdaten ändern |
|---|---|---|---|---|---|
| Alle Angemeldeten | ✓ | — | ✓ | — | — |
| `PersonnelApprove` | ✓ | ✓ | ✓ | ✓ | — |
| `PersonnelRecords` | ✓ | — | ✓ | ✓ | ✓ |

## Was bewusst fehlt

| Fehlt | Warum |
|---|---|
| Löschfristen für Krankmeldungen | Brauchen eine Entscheidung des Hauses (Aufbewahrung nach Steuer- und Sozialrecht gegen Datensparsamkeit). Offen, siehe unten. |
| Bildungsurlaub-Kontingent (Berlin: 10 Tage in 2 Jahren) | Landesrecht, unterschiedlich. Bildungsurlaub ist eine Art, kein eigenes Konto. |
| Urlaubssperren (Premierenwoche) | Ein Grund für eine Ablehnung, kein Automatismus. |
| Anhänge (AU-Bescheinigung als Datei) | Die Bescheinigung gehört in die Personalakte des Steuerbüros, nicht in eine Ablage, die Gruppen sehen. |
| Arbeiten im Ausland, Dienstreisen, A1, Länder-Risikoklassen | Kommen an Touring (ADR-0010): Wer reist, reist meist mit einer Tournee. Eigenes ADR. |
| On- und Offboarding | Kommen als Aufgabenvorlagen (ADR-0007). Eigenes ADR. |

## Offen

- **Löschfristen**: Wann werden Krankmeldungen gelöscht oder auf „abwesend“ reduziert?
- **Wer im Haus entscheidet?** Im Haus klären, wer `PersonnelApprove`
  bekommt. Die mitgelieferten Rollen sind ein Vorschlag.
- **Datenschutz-Folgenabschätzung**: Mit Gesundheitsdaten im System braucht das Haus
  eine Einschätzung, ob eine DSFA nötig ist, und einen Eintrag im
  Verarbeitungsverzeichnis. Das ist Sache des Hauses, nicht der Software. Wir sollten
  es beim Ausrollen ansprechen.

## Was diese Entscheidung umstoßen würde

- Ein Haus braucht **Abwesenheiten in Stunden** (Gleitzeit-Modelle mit stundenweisem
  Ausgleich). Dann reichen ganze und halbe Tage nicht mehr.
- Die Lohnbuchhaltung will die Meldungen **elektronisch** (eAU-Abruf, DATEV Lohn).
  Das ist eine Zulassungsfrage wie in ADR-0002 und gehört nicht hierher.
