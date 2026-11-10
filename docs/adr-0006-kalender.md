# ADR-0006: Kalender — Regel statt Kopien, Ortszeit statt UTC, Jahr statt Monat

**Stand:** 30.10.2026 · **Status:** entschieden · **Gilt für:** souffleur 2

## Entscheidung

Der Kalender ist ein eigenes Modul. Drei Festlegungen tragen ihn:

1. **Eine Serie ist eine Regel, keine Menge von Kopien.** Gespeichert wird „jeden
   Dienstag und Donnerstag 19:30, bis 30.06."; die einzelnen Termine rechnet der
   Server beim Abfragen aus.
2. **Eine Serie rechnet in Ortszeit.** Gespeichert wird `19:30` plus `Europe/Berlin`,
   nicht ein UTC-Zeitpunkt.
3. **Die Leitansicht ist das Jahr**, nicht der Monat: Monate als Spalten, Tage als
   Zeilen, Spannweite frei zwischen 3 und 24 Monaten.

## 1. Warum eine Regel und keine Kopien

Der naheliegende Weg wäre, beim Anlegen einer Serie alle Termine als Datensätze zu
schreiben. Er scheitert an zwei Stellen:

- **Unbefristete Serien.** „Jeden Montag Dienstbesprechung" hat kein Ende. Es gibt
  keine Zahl, bis zu der man schreiben könnte, die nicht willkürlich wäre.
- **Nachträgliche Änderungen.** Verschiebt sich die Probenzeit, müssten alle künftigen
  Kopien angefasst werden — und zwar genau die, die noch nicht einzeln geändert
  wurden. Diese Unterscheidung lässt sich an Kopien nicht mehr treffen.

Stattdessen: Die Regel liegt in `EventSeries`, und ein `CalendarEvent` mit `SeriesId`
entsteht nur dort, wo ein einzelner Termin **von der Regel abweicht** — verschoben,
umbenannt, abgesagt. Beim Abfragen eines Zeitraums rechnet der Server die Regel aus
und legt die Abweichungen darüber.

Das ist auch der Weg von RFC 5545 (iCalendar) mit `RRULE`, `EXDATE` und
`RECURRENCE-ID`. Wer sich daran hält, kann später exportieren, ohne das Modell zu
drehen.

### Die Grenze

Eine Regel ohne Ende kann beliebig viele Termine ergeben. Der Server beantwortet
deshalb nur **Zeiträume**, nie „alle Termine dieser Serie", und deckelt die Zahl der
ausgerechneten Vorkommen je Serie und Anfrage. Ohne diesen Deckel genügt eine
tägliche Serie und ein Zeitraum von hundert Jahren, um die Anwendung lahmzulegen.

## 2. Warum Ortszeit und nicht UTC

Alles andere in souffleur liegt in UTC, und das bleibt so. Für Serien geht es nicht.

Eine Probe, die „immer 19:30" ist, ist im Winter 18:30 UTC und im Sommer 17:30 UTC.
Wer die Serie als UTC-Zeitpunkt plus Intervall speichert, bekommt **nach der
Zeitumstellung einen um eine Stunde verschobenen Spielplan** — der halbe Betrieb
stünde eine Stunde falsch da, und zwar lautlos.

Deshalb speichert eine Serie `StartDate` (`DateOnly`), `StartTime` (`TimeOnly`) und
`TimeZoneId`. Jedes ausgerechnete Vorkommen wird einzeln von Ortszeit nach UTC
umgerechnet.

Die beiden Sonderfälle der Umstellung sind benannt und behandelt:

| Fall | Was passiert | Was souffleur tut |
|---|---|---|
| Frühjahr, 02:30 | die Stunde gibt es nicht | der Termin rückt auf 03:00 |
| Herbst, 02:30 | die Stunde gibt es zweimal | es gilt die erste (Sommerzeit) |

Einzeltermine bleiben `DateTimeOffset` in UTC: Ein einmaliger Termin hat einen festen
Zeitpunkt, kein wiederkehrendes Zifferblatt.

## 3. Warum das Jahr die Leitansicht ist

Ein Monatskalender beantwortet die Frage „was ist nächste Woche?". Die Fragen eines
Theaters lauten anders:

- Wann liegt die probenfreie Zeit, in der die Werkstatt an die Bühne kann?
- Wie viele Vorstellungen stehen bis zum Spielzeitende, und wie verteilen sie sich?
- Kollidiert die Wiederaufnahme mit dem Gastspiel in Hamburg?

Keine davon passt in einen Monat. Die Disposition in Theater und Musik arbeitet
deshalb seit jeher mit dem **Jahresraster**: Monate nebeneinander, Tage untereinander,
ein Blick über die ganze Spielzeit.

Zwei Eigenschaften machen es brauchbar:

- **Die Spannweite ist frei.** Eine Spielzeit läuft von August bis Juli, nicht von
  Januar bis Dezember. Eine Produktion plant man zwölf Monate im Voraus, eine
  Tournee achtzehn. Fest verdrahtete zwölf Monate ab Januar wären für fast jede
  dieser Fragen der falsche Ausschnitt.
- **In der Zelle steht, was los ist** — nicht nur ein Punkt. Jeder Tag zeigt seine
  Termine mit Kurztext und der Farbe seiner Kategorie. Erst dadurch ersetzt die
  Ansicht den Blick in den Monat, statt ihn anzukündigen.

### Die anderen drei

Monat, Woche und Tag stehen daneben, weil sie andere Fragen beantworten. Die
Aufteilung ist keine Geschmacksfrage:

| Ansicht | Antwortet auf | Einheit |
|---|---|---|
| Jahr | wie verteilt sich die Spielzeit? | der Tag im Monatsraster |
| Monat | was steht die nächsten Wochen an? | der Tag in der Woche |
| Woche | wie lange dauert es, was überschneidet sich? | die Stunde |
| Tag | was genau ist heute, mit Ort und Zeit? | die Stunde |

Woche und Tag sind **dieselbe Komponente** — ein Tag ist eine Woche mit einer
Spalte. Beide zeigen, was das Jahresraster nicht zeigen kann: Dauer und
Kollision. Überlappende Termine stehen dort nebeneinander statt übereinander,
denn eine verdeckte Kollision ist genau die, die man übersieht.

Ganztägiges und Mehrtägiges steht in beiden über dem Stundenraster, nicht
darin: Ein viertägiges Gastspiel als Balken durch den Stundenplan zu ziehen
hieße, die Spalte für alles andere zu verlieren.

Alle vier teilen einen Ankertag. Wer die Ansicht wechselt, bleibt dort, wo er
war — und der Zeitraum, den der Server liefert, richtet sich nach der Ansicht,
nicht umgekehrt.

Das Jahr bleibt der Einstieg.

## Kategorien

Terminkategorien sind ein Katalog nach ADR-0005: Das Haus legt sie selbst an, benennt
sie um und gibt ihnen die Farbe, in der sie im Jahresraster erscheinen.

## Ressourcen: eine eigene Entität, ihre Art ein Katalog

Ein Termin belegt etwas — die Bühne, die Probebühne, die Werkstatt, den
Transporter. Die Frage war, ob das ein Katalog nach ADR-0005 wird oder eine
eigene Entität. Es wird **beides, aber an verschiedenen Stellen**:

| | Was | Warum dort |
|---|---|---|
| **Eigene Entität** | die Ressource: Großer Saal, Probebühne 1 | trägt Verhalten: exklusiv oder teilbar, Platzzahl — die Belegungsprüfung rechnet damit |
| **Katalog** | ihre Art: Bühne, Probebühne, Werkstatt, Raum, Fahrzeug, Technik | trägt nur Name, Farbe, Reihenfolge |

Die Trennlinie ist dieselbe wie in ADR-0005: Eine Liste, aus der nur ausgewählt
wird, ist ein Katalog. Etwas, von dem eine Entscheidung des Servers abhängt,
nicht.

Der Fall, der das entscheidet, ist `IsExclusive`: Ein Saal lässt sich nicht
zweimal gleichzeitig vergeben, ein Scheinwerferpark schon. Das ist keine
Eigenschaft der *Art* — unter „Technik" steht beides —, sondern der einzelnen
Ressource. Ein Katalogeintrag könnte das nicht tragen, ohne zu etwas anderem zu
werden.

Eine Belegung (`ResourceBooking`) hängt entweder an einem Termin oder an einer
Serie, nie an beidem. Bei der Serie ist sie eine Aussage über die **Regel**: Die
Probe belegt die Probebühne an jedem ihrer Termine, und welche Tage das sind,
rechnet die Regel aus.

### Was die Belegungsprüfung meldet — und was nicht

| Lage | Konflikt? | Warum |
|---|---|---|
| zwei feste Termine, derselbe Saal, überlappend | ja | der Fall, um den es geht |
| einer davon ist **vorgemerkt** | ja | eine Option blockiert den Saal, sonst wäre sie keine |
| einer davon ist **abgesagt** | nein | ein abgesagter Termin gibt die Bühne frei |
| die Ressource ist **nicht exklusiv** | nein | Scheinwerfer gibt es mehrfach |
| 18:00–20:00 und 20:00–22:00 | nein | Berührung ist keine Überschneidung |
| derselbe Termin, nur verschoben | nein | sein eigener alter Stand steht ihm nicht im Weg |
| ein Vorkommen der Serie, das **überschrieben** wurde | nein | an seiner Stelle gilt der eigene Datensatz |

Ein Konflikt **blockiert nicht endgültig**. Der Server antwortet mit 409 und
nennt die Gegentermine als Daten, nicht als Satz; mit `force` geht derselbe
Aufruf durch. Der Grund: Die Disposition weiß manchmal, dass sie doppelt belegt,
und löst es später auf. Was sie nicht darf, ist es *übersehen* — deshalb der
ausdrückliche zweite Knopf statt einer Warnung, die man wegklickt.

Drei Festlegungen hängen daran:

- **Die Abweichung erbt die Belegung der Serie.** Wird die Probe von Dienstag
  auf Mittwoch verlegt, geht die Probebühne mit. Sonst gäbe die verschobene
  Probe den Raum lautlos frei, und der Dienstag stünde als belegt da.
- **Geprüft wird nach vorn, nicht zurück.** Eine Serie ohne Ende belegt die
  Bühne für immer; die Prüfung schaut 400 Tage voraus. Eine Doppelbelegung von
  letztem Jahr lässt sich nicht mehr auflösen, und sie zu melden hielte nur
  davon ab, die Regel zu ändern.
- **Gelöscht wird nicht, abgeschaltet schon.** Eine Ressource, die ein Termin
  trägt, bleibt — sonst stünde im Spielplan von 2023 eine Bühne ohne Namen.
  Dasselbe Prinzip wie bei den Listen (ADR-0005).

Mitgeliefert werden die *Arten*, nicht die Ressourcen: Welche Bühnen ein Haus
hat, weiß nur das Haus.

## Der Kalender führt zusammen, was anderswo entsteht

Ein Termin in souffleur kommt nicht nur aus dem Kalender:

| Quelle | Beispiel | Stand |
|---|---|---|
| Kalender selbst | Probe, Sitzung, Sperrtermin | gebaut |
| Aufgaben | Frist einer Aufgabe | gebaut, siehe ADR-0007 |
| artwork | Vorstellungen aus dem Spielplan des Hauses | lesend, geplant |
| Ticketing | Veranstaltungen mit Vorverkauf | lesend, geplant |

Diese Quellen werden **nicht in den Kalender kopiert**. Der Abfrageweg ist darauf
angelegt, mehrere Quellen für denselben Zeitraum nebeneinander zu stellen; jeder
Eintrag trägt, woher er stammt, und was nicht aus dem Kalender kommt, ist dort nicht
änderbar. Eine Kopie wäre sofort veraltet und niemand wüsste, welche Fassung gilt.

### Aufgaben und Zeitstrahl

Die Aufgaben brauchen einen eigenen Zeitstrahl — Vorgänge mit Anfang, Ende und
Abhängigkeit, die sich überlappen. Das ist **nicht** dieselbe Ansicht wie das
Jahresraster, auch wenn beide Zeit auftragen:

| | Jahresraster | Zeitstrahl der Aufgaben |
|---|---|---|
| Frage | was ist an diesem Tag los? | wie lange dauert das, und was hängt daran? |
| Einheit | der Tag | der Vorgang |
| Richtung | Tage untereinander, Monate nebeneinander | Vorgänge untereinander, Zeit nach rechts |

Beide auf dasselbe Bild zwingen zu wollen, hieße eines von beiden schlecht zu
machen. Was sie teilen, ist die **Datenbasis**: Eine Aufgabe mit Frist ist im
Kalender ein Termin, und ein Produktionszeitraum im Zeitstrahl kann als
Sperrtermin im Raster erscheinen.

Deshalb liegt die Frist an der Aufgabe und wird vom Kalender gelesen, nicht
kopiert. Der Zeitstrahl wird mit dem Aufgabenmodul gebaut; der Kalender ist
darauf vorbereitet, ihn als weitere Quelle aufzunehmen.

Umgekehrt gibt der Kalender nach draußen ab — iCalendar-Abonnement, damit die Leute
des Hauses ihren Dienstplan im eigenen Telefon sehen. Fremde Kalender **herein** zu
lesen, ist eine andere Frage: Wer die privaten Termine der Belegschaft einsammelt,
braucht dafür mehr als eine technische Entscheidung. Das bleibt offen.

## Teilnehmer: aus den Kontakten, geprüft wie ein Raum

> **Nachtrag 02.11.2026 — Wortwahl.** In diesem Abschnitt stand zweimal
> „Besetzung". Am Theater ist das die Rollenbesetzung eines Stücks oder die
> Besetzung eines Orchesters: wer welche Partie spielt. Gemeint ist hier die
> **Einteilung** zu einem Dienst — wer an diesem Abend eingeteilt ist. Zwei
> verschiedene Dinge: Die Besetzung hängt an der Produktion, die Einteilung am
> Termin. Die Stellen unten sind berichtigt; die Entscheidung selbst bleibt
> unverändert. Eine Besetzung im eigentlichen Sinn kann souffleur nicht
> abbilden, solange es keine Produktion als Entität gibt.


Wer bei einem Termin dabei ist, kommt aus dem Adressbuch — es gibt nur eines
(siehe `Contact`). Die Schauspielerin auf der Probenliste ist dieselbe, die am
Monatsende ihr Honorar bekommt; eine zweite Personenliste daneben würde sofort
auseinanderlaufen.

Drei Dinge hängen an einer Beteiligung:

| Feld | Wofür | Woher |
|---|---|---|
| Person | wer | Kontakt (`Contact`) |
| Funktion | was er an **diesem** Abend tut: Regie, Inspizienz, Abenddienst | Katalog (ADR-0005) |
| Zusage | eingeteilt · zugesagt · abgesagt | fest, drei Stufen |

Die Funktion ist **nicht** die Rolle aus dem Adressbuch. Dort steht, in welcher
Eigenschaft jemand mit dem Haus zu tun hat; hier steht, was er heute Abend
macht. Dieselbe Person kann heute Inspizienz sein und morgen Abenddienst.

Die drei Stufen der Zusage gibt es, weil souffleur **nichts verschickt**. Es
gibt kein Einladungsverfahren; die Disposition ruft an und trägt ein, was dabei
herauskam. Ohne diese Unterscheidung bliebe offen, ob eine Einteilung steht
oder nur gewünscht ist.

### Personen laufen durch dieselbe Belegungsprüfung wie Räume

Niemand ist an zwei Orten. Eine Person ist damit immer exklusiv — anders als
eine Ressource, die geteilt werden kann (ein Scheinwerferpark). Sonst gelten
dieselben Regeln wie oben: Abgesagtes zählt nicht, Vorgemerktes schon, die
Abweichung einer Serie erbt deren Einteilung, und mit `force` lässt sich die
doppelte Einteilung durchsetzen.

Deshalb heißt der Konflikt nicht mehr „Ressourcenkonflikt", sondern trägt, was
belegt ist: eine Bühne oder eine Person. Die Disposition will beides in
derselben Meldung sehen, nicht in zweien.

### Was daraus folgt: das persönliche Abonnement

Ein Abonnement lässt sich auf eine Person filtern. Damit wird aus dem Spielplan
ein **Dienstplan**: Im Telefon stehen genau die Termine, zu denen man kommen
muss, und nichts sonst. Das war die offene Frage am Ende des vorigen
Abschnitts — sie ist damit beantwortet, ohne dass es einen eigenen Begriff
„mein Kalender" braucht.

### Namen gehen (noch) nicht in den Feed

Der Feed nennt **keine** Teilnehmer — weder als `ATTENDEE` noch im Text. Das
ist eine Entscheidung, keine Lücke: Wer ein Abonnement bei Google oder Apple
einträgt, legt dessen Inhalt auf fremde Server. Die Namen der Belegschaft samt
Dienstzeiten gehören nicht ungefragt dorthin. Ein Haus, das es will, soll es
einschalten können — diese Wahl gibt es noch nicht.

## Abonnieren statt exportieren

Was der Kalender nach draußen gibt, ist **kein Export einer Datei**, sondern
eine Adresse, die sich in jeder Kalenderanwendung abonnieren lässt — Apple
Kalender, Outlook, Google, Thunderbird. Ändert sich in souffleur ein Termin,
ändert er sich dort beim nächsten Abgleich. Eine heruntergeladene `.ics` wäre
das Gegenteil: veraltet in dem Moment, in dem die Disposition etwas verschiebt.

```
https://<haus>/api/calendar/feed/<token>.ics
```

### Die Adresse ist der Schlüssel

Eine Kalenderanwendung kann sich nicht anmelden. Die Berechtigung steckt
deshalb im Token in der Adresse: Wer sie hat, sieht den Ausschnitt. Daraus
folgt dreierlei:

- Das Token kommt aus dem kryptografischen Zufall, 24 Bytes — wer es raten
  will, rät an einem Passwort.
- Es lässt sich **zurückziehen**. Ein neues Token macht die alte Adresse tot;
  alle, die abonniert haben, müssen die neue eintragen. Das ist der einzige
  Weg, ein weitergegebenes Abonnement wieder einzufangen, und die Oberfläche
  sagt das auch so.
- Es steht im Klartext in der Datenbank. Wer die lesen kann, liest ohnehin die
  Termine, die das Token schützt — dafür lässt sich die Adresse später noch
  einmal anzeigen, statt nach dem Anlegen für immer verloren zu sein.

Eine unbekannte und eine zurückgezogene Adresse bekommen dieselbe Antwort:
404. Wer Adressen durchprobiert, soll am Statuscode nichts ablesen können.

### Mehrere Adressen, nicht eine mit Parametern

Ein Abonnement trägt seinen Ausschnitt in sich — Kategorien, Ressourcen, ob
Abgesagtes mitgeht. Der Spielplan fürs Ensemble, der Große Saal für die
Technik, die Fristen für die Geschäftsführung: je ein eigenes Abonnement.
Parameter in der Adresse wären dasselbe in unübersichtlich, und zurückziehen
ließe sich ein einzelner Ausschnitt dann nicht mehr.

### Die Regel geht hinaus, nicht ihre Ergebnisse

Eine unbefristete Probenserie als ausgerechnete Termine zu schreiben, ginge
nicht — und bei zwei Jahren aufzuhören, hieße zu lügen. Der Feed gibt deshalb
`RRULE` weiter. Genau dafür ist das Modell gebaut (siehe oben, Punkt 1).

Was dabei leicht falsch wird, und wie es gelöst ist:

| Fall | Regel |
|---|---|
| `DTSTART` | der **erste wirkliche Termin**, nicht das gespeicherte Startdatum — RFC 5545 zählt `DTSTART` immer als Vorkommen mit |
| Serien | Ortszeit mit `TZID` plus `VTIMEZONE` — in UTC wäre der Spielplan nach der Zeitumstellung um eine Stunde verschoben |
| Einzeltermine | UTC, ohne Zeitzonenangabe — ein einmaliger Termin hat einen festen Zeitpunkt |
| verschoben | eigener Eintrag mit `RECURRENCE-ID` auf der **alten** Zeit, sonst steht er zweimal da |
| abgesagt | `STATUS:CANCELLED` mit `RECURRENCE-ID`, oder `EXDATE`, wenn das Abonnement Abgesagtes weglässt |
| `UNTIL` | als Zeitpunkt in UTC, nicht als Tag |
| alle zwei Wochen | mit `WKST=MO`, sonst liegt jeder zweite Termin eine Woche daneben |
| Zeilenlänge | 75 **Oktette**, umgebrochen ohne ein Zeichen zu zerschneiden |

Der erste wirkliche Termin wird mit derselben Regelauswertung gesucht, die
auch das Jahresraster füllt. So stimmen Feed und Oberfläche überein, statt
sich in Randfällen auseinanderzuleben.

Einzeltermine reichen 180 Tage zurück: Was länger her ist, interessiert
niemanden mehr und machte den Feed mit jedem Jahr langsamer. Serien stehen
davon unberührt — sie kosten eine Zeile, egal wie lange es sie gibt.

Ein `ETag` spart den Abgleich: Hat sich nichts geändert, antwortet der Server
mit 304. Bei zwanzig Telefonen, die stündlich fragen, ist das der Unterschied
zwischen Last und keiner.

## Zwei Fallen, die beim Bauen auffielen

Beides steht hier, weil es sich wiederholen wird:

- **`DTSTART` muss ein wirklicher Termin sein.** RFC 5545 zählt ihn immer als
  Vorkommen mit. Eine Serie „dritter Freitag im Monat", angelegt an einem
  Dienstag, erzeugte sonst in jedem Telefon eine Sitzung, die es nicht gibt.
- **Die Kennungen im Feed sind stabil und tragen `@souffleur`.** Sie sind
  dafür da, dass Kalenderanwendungen Änderungen zuordnen statt zu verdoppeln.
  Sie haben einen zweiten Nutzen: Wer eine Datei versehentlich *importiert*
  statt sie zu abonnieren, kann die Termine daran wieder herausfiltern. Das
  ist kein Grund, sie zu ändern.

## Nur eine Richtung: hinaus

Fremde Kalender **hereinzulesen** ist entschieden und zwar dagegen. Der
Kalender gibt ab, er sammelt nicht ein.

Der Grund ist nicht technisch. Wer die Kalender der Belegschaft anschließt,
holt sich Arzttermine, Bewerbungsgespräche und Elternabende ins Haus — Daten,
die niemanden im Betrieb etwas angehen und für die es eine Rechtsgrundlage
bräuchte, die ein Dienstplan nicht hergibt. Der Nutzen wäre ein bequemeres
Finden freier Termine; der Preis wäre eine Sammlung, die das Haus weder
braucht noch verantworten will.

Was das Haus stattdessen hat: Es gibt den Dienstplan heraus, und jeder trägt
ihn in seinen eigenen Kalender ein. Dort liegt er neben den privaten Terminen,
ohne dass souffleur sie je sieht. Die Abwägung fällt damit auf der richtigen
Seite — beim Menschen, nicht im Server.

## Dauerlaeufe im Jahresraster

Ein mehrtaegiger Termin steht in jedem Tag, ueber den er laeuft -- ein Gastspiel
gehoert vom Anreise- bis zum Abreisetag ins Raster, daran plant man.

Fuer eine Installation ueber Jahre stimmt das nicht mehr. Bei einem der
Haeuser lief eine Installation ueber vier Jahre, eine zweite ueber fuenf.
Beide standen in jeder Zelle des Jahresrasters; die Proben und
Gastspiele dazwischen waren nicht mehr zu sehen.

Im Jahresraster erscheint ein Termin ab acht Tagen Dauer deshalb nur noch an
seinem ersten Tag und danach einmal je Monatsspalte, gekennzeichnet mit ↔ und
dem Enddatum im Tooltip. Monat, Woche und Tag bleiben unveraendert: Wer den
Dezember ansieht, will wissen, dass die Installation laeuft.

Die Grenze liegt bei acht Tagen, weil eine Gastspielwoche noch an jedem Tag
stehen soll. Sie steht in der Ansicht, nicht in den Daten -- es ist eine Frage
der Darstellung, keine des Termins.

## Offen

- Teilnehmer im Feed sichtbar machen, als Entscheidung des Hauses (siehe oben).
