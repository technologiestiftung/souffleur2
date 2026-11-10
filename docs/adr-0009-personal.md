# ADR-0009: Personal — Dienste, Zeiten, Künstlersozialabgabe

**Stand:** 03.11.2026 · **Status:** entschieden · **Gilt für:** souffleur 2

## Entscheidung

Das Modul Personal beantwortet drei Fragen an dieselben Leute:

| Ansicht | Frage |
|---|---|
| **Dienste** | Wer ist eingeteilt, und wo fehlt jemand? |
| **Zeiten** | Was wurde gearbeitet? |
| **Künstlersozialabgabe** | Was folgt daraus für die Kasse? |

Drei Festlegungen tragen das Modul, und jede hätte auch anders ausfallen
können.

## 1. Gestempelt wird auf den Kontakt, nicht auf das Benutzerkonto

Der Kontakt trägt seit diesem Modul eine `UserId`. Ohne diese Verbindung wäre
derselbe Mensch zweimal im System: einmal als Kontakt, der Dienst hat, einmal
als Konto, das stempelt — und die eigene Zeit wäre nicht die eigene.

Es gibt **ein** Adressbuch (siehe `Contact`). Die Schauspielerin auf der
Probenliste ist dieselbe, die am Monatsende ihr Honorar bekommt, und dieselbe,
die ihre Stunden sieht.

Verbunden wird im Adressbuch und **nur von der Verwaltung**: Wer sich selbst
ein Konto zuordnen könnte, könnte sich fremde Zeiten zuordnen. Nicht jeder
Kontakt hat ein Konto — die Aushilfe am Einlass stempelt nicht selbst, ihre
Zeit trägt die Disposition nach.

## 2. Der Bedarf hängt an der Terminkategorie

Verworfen wurde die naheliegende Alternative: eine eigene Schichtliste mit
eigenen Zeiten, wie artwork sie führt.

Dienste entstehen im Theater an Vorstellungen und Proben, und die stehen schon
im Kalender. Eine zweite Zeitachse daneben wäre eine zweite Wahrheit darüber,
was an einem Abend stattfindet — und zwei Wahrheiten über denselben Abend sind
schlimmer als eine ungenaue.

Je Terminart **ein** Bedarf. Zwei Vorlagen für dieselbe Kategorie müssten
gegeneinander abgewogen werden, und die Regel dafür könnte niemand im Kopf
behalten.

Der Gewinn daran ist die Lücke: Eine Vorstellung, an der niemand eingeteilt
ist, taucht im Dienstplan auf, weil ihre Terminart einen Bedarf hat. In einer
Liste der Eingeteilten wäre sie unsichtbar.

**Besetzt wird direkt aus dem Plan** — aber nur bei einem einzelnen Termin.
Ein Serienvorkommen trägt keine eigene Einteilung: Sie hängt an der Serie, und
ein Eingriff am einzelnen Abend verlangt eine Abweichung (siehe ADR-0006). Der
Plan sagt das an der Stelle, statt einen Knopf anzubieten, der etwas anderes
täte als erwartet.

## 3. Das Arbeitszeitgesetz erscheint als Hinweis, nicht als Fehler

Eine Schicht von elf Stunden hat stattgefunden, auch wenn sie nicht hätte
stattfinden dürfen (§ 3 ArbZG). Sie zu verschweigen wäre schlechter, als sie
zu melden.

Abgewiesen wird deshalb nur, was die Aufzeichnung selbst unbrauchbar macht:

| Abgewiesen | Warum |
|---|---|
| Zweimal kommen | Wer schon läuft, läuft. |
| Überschneidende Zeiten derselben Person | Fast immer ein vergessenes Gehen — und es verdoppelte den Saldo. |
| Spanne über 24 Stunden | Dito. |
| Korrektur ohne Grund | Eine stillschweigend verschobene Arbeitszeit ist der Grund, warum Aufzeichnungen angezweifelt werden. |

Eine **laufende** Zeit lässt sich verwerfen, eine abgeschlossene nur
richtigstellen: Was einmal als gearbeitet dastand, soll nicht spurlos
verschwinden können.

**souffleur ist kein zertifiziertes Zeiterfassungssystem.** Es zeichnet auf.
Ob die Aufzeichnung vollständig ist und ob die Pausen genommen wurden, bleibt
beim Haus — das ist eine Frage der Organisation und der Aufsicht, keine der
Software.

## 4. Die Künstlersozialabgabe rechnet, entscheidet aber nicht

Das Kennzeichen sitzt am Kontakt (`ContactBankDetails
.IsSubjectToArtistsSocialFund`). souffleur zählt zusammen, was das Haus
gekennzeichnet hat.

**Kein Abgabesatz wird mitgeliefert.** Er wird jährlich im Bundesanzeiger
veröffentlicht; eine Zahl in der Software wäre spätestens im nächsten Jahr
falsch, ohne dass es jemand merkt. Ohne Satz steht die Bemessungsgrundlage da
und keine Abgabe.

**Bemessungsgrundlage ist das Entgelt ohne Umsatzsteuer.** Gerechnet wird aus
den Belegen und nicht aus den Buchungen: Ein Beleg kann erfasst und noch nicht
gebucht sein, und bis zum Meldeschluss im März ist das der Normalfall. Wo eine
Buchung dranhängt, zieht die Rechnung die ausgewiesene Steuer ab. Wo keine
hängt, steht brutto — und diese Belege werden gezählt und angezeigt, damit die
Zahl nicht genauer aussieht, als sie ist.

Neben der Summe steht, **wer künstlerisch mit dem Haus zu tun hat, Geld
bekommen hat und nicht gekennzeichnet ist**. Ob jemand abgabepflichtig ist,
hängt an der Selbständigkeit, an der Art der Leistung und manchmal an einer
Einzelfallprüfung der Kasse. Das entscheidet das Haus; souffleur sorgt dafür,
dass die Frage gestellt wird.

Die Meldung selbst geht weiterhin über das Portal der Künstlersozialkasse.

## Wer was sieht

| | Eigene Zeiten | Fremde Zeiten | Bedarf setzen | Künstlersozialabgabe |
|---|---|---|---|---|
| Staff | ✓ | — | — | — |
| Scheduling | ✓ | ✓ | ✓ | — |
| Accounting | ✓ | — | — | ✓ |
| Management · Administrator | ✓ | ✓ | ✓ | ✓ |

Arbeitszeiten sind Personaldaten: Wer wann da war, geht die Kollegin am
Nebentisch nichts an. Honorarsummen je Person sind Personaldaten und
Finanzzahlen zugleich.

## Was bewusst fehlt

| Fehlt | Warum |
|---|---|
| Lohnabrechnung | Die ITSG-Zertifizierung ist eine Zulassung, kein Entwicklungsaufwand — siehe ADR-0002. |
| ~~Urlaubskonten und Krankmeldungen~~ | Ein Haus hat gefragt — seit 04.11.2026 in ADR-0012. |
| ~~Überstundensalden über Monate~~ | Mit den Vertragsdaten aus ADR-0012 gibt es ein Stundenkonto je Jahr. |
| Zuschläge (Nacht, Sonntag, Feiertag) | Gehören zur Abrechnung, nicht zur Erfassung. |

## Was diese Entscheidung umstoßen würde

- Ein Haus führt Dienste, die an **keinem** Termin hängen (Pforte, Reinigung,
  Rufbereitschaft). Dann braucht es doch eine eigene Schicht — und die
  Begründung unter 2 fällt.
- Eine Betriebsvereinbarung verlangt eine zertifizierte oder manipulations-
  sichere Zeiterfassung. Dann ist souffleur das falsche Werkzeug dafür, und die
  Zeiten kommen aus einem System, das die Zulassung hat.
