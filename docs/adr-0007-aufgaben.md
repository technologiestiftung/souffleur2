# ADR-0007: Aufgaben — eine Liste, die benutzt wird

**Stand:** 31.10.2026 · **Status:** entschieden · **Gilt für:** souffleur 2

## Entscheidung

Das Aufgabenmodul ist eine **flache Liste mit einer Ebene Untergliederung**,
sortiert nach Frist. Eine Aufgabe trägt vier Angaben, die sie von einer Notiz
unterscheiden: Titel, Stand, Zuständigkeit und Frist. Die Frist gehört der
Aufgabe; im Kalender erscheint sie, ohne dort zu liegen.

## Warum so wenig

Der Reflex bei Aufgabenverwaltung ist, nachzubauen, was Jira kann: Boards,
Sprints, Punkte, Abhängigkeiten, Unteraufgaben in beliebiger Tiefe. Für ein
Haus mit zwölf Leuten ist das die sicherste Art, dass niemand sie benutzt und
das Eigentliche wieder auf Zetteln steht.

Deshalb ist weggelassen, was sich nicht in einem Satz begründen lässt:

| Weggelassen | Warum |
|---|---|
| Priorität | Die Frist ist die Priorität. Eine zweite Achse lädt zum Sortieren statt zum Arbeiten ein. |
| Beliebige Schachtelung | Eine Checkliste in einer Checkliste versteht hinterher niemand. Zwei Ebenen, mehr nicht. |
| Abhängigkeiten in der Liste | Gehören zum Zeitstrahl. Dort sind sie jetzt — siehe unten. |
| Mehrere Zuständige | „Alle" heißt niemand. Eine Aufgabe hat eine Person, die sie übernimmt. Seit 04.11.2026 gibt es daneben **Beteiligte** — siehe unten; das ist keine zweite Zuständigkeit. |
| Aufwand und Ist-Zeit | Zeiterfassung ist ein eigenes Thema mit eigenen Regeln (Personal). |

## Die vier Stufen

`Offen · In Arbeit · Wartet · Erledigt` — und `Verworfen` daneben.

„Wartet" würde man am ehesten weglassen und am wenigsten entbehren: Im
Kulturbetrieb hängt vieles an einer Zusage von außen — der Förderstelle, der
Spielstätte, der Agentur. Ohne eigene Stufe steht so etwas als „offen" in der
Liste und sieht aus wie Versäumnis.

„Verworfen" gibt es, damit nichts gelöscht werden muss. Dasselbe Prinzip wie
beim Storno und beim Abschalten von Katalogeinträgen (ADR-0005): In drei
Monaten fragt jemand, was aus der Sache geworden ist.

Der Abschlusszeitpunkt wird vom Dienst geführt, nicht vom Aufrufer. Sonst
steht irgendwann „erledigt" ohne Datum da — oder ein Datum an etwas, das
wieder geöffnet wurde.

## Zuständig ist ein Kontakt, kein Benutzerkonto

Vieles im Kulturbetrieb erledigt, wer kein Konto hat: die freie Ausstatterin,
die Agentur, der Steuerberater. Ein Konto hat, wer souffleur benutzt; eine
Aufgabe hat, wer sie übernimmt. Das ist nicht dieselbe Menge.

Es gibt deshalb **ein** Adressbuch, auch hier (siehe `Contact`). Dieselbe
Person, die auf der Probenliste steht und am Monatsende ihr Honorar bekommt,
ist die, die den Pressetext schreibt.

## Die Frist im Kalender: gelesen, nicht kopiert

Eine Frist ist keine Verabredung, sondern etwas, das jemand tun muss. Sie
gehört zur Aufgabe. Im Kalender erscheint sie trotzdem — ADR-0006 hatte den
Platz dafür freigehalten, jetzt ist er besetzt:

- Der Kalender liest die Fristen beim Abfragen eines Zeitraums. Es gibt
  **keine** Kopie; eine wäre sofort veraltet.
- Der Eintrag trägt `Source = Tasks` und die Id der Aufgabe. Die Oberfläche
  kennzeichnet ihn und verweist zurück — ändern lässt er sich nur dort, wo er
  entsteht.
- Die Zuständigkeit zählt wie eine Beteiligung. Dadurch greift der
  Personenfilter auch auf Fristen: „Was habe ich diese Woche?" umfasst beides.
- **Nur Offenes.** Was erledigt oder verworfen ist, schränkt die Planung nicht
  mehr ein und füllte das Raster mit Vergangenem.
- Ist das Modul abgeschaltet, erscheinen keine Fristen — der Kalender fragt
  den Schalter, nicht den Dienst.

Fristen gehen auch in das **Abonnement** hinaus (ADR-0006), mit eigener
Kennung `task-…@souffleur`. Eine Frist, die im Telefon fehlt, ist eine
verpasste Frist.

Im Vorführbestand zeigt sich das an zwei Einträgen, die früher Termine waren:
Förderantrag und Verwendungsnachweis stehen jetzt in den Aufgaben und
erscheinen im Kalender, ohne dort zu liegen.

## Der Zeitstrahl: Dauer statt Tage

Eine andere Frage als das Jahresraster des Kalenders, und deshalb eine eigene
Ansicht (ADR-0006, „Aufgaben und Zeitstrahl"): Dort geht es darum, was an
einem Tag los ist — hier darum, wie lange etwas dauert und was daran hängt.
Vorgänge untereinander, Zeit nach rechts.

- Was einen **Anfang oder eine Frist** hat, bekommt einen Balken. Nur eine
  Frist heißt: ein Stichtag, als Raute statt als Balken.
- Was **weder noch** hat, erscheint nicht — wird aber gezählt und angezeigt.
  Stillschweigend verschwinden darf nichts.
- Ein Balken erscheint, wenn er den Ausschnitt **berührt**, nicht wenn er
  darin liegt. Sonst verlöre man einen dreimonatigen Vorgang, sobald man in
  seine Mitte blickt.

### Abhängigkeit: eine Art, und kein Umplanen

Die Projektplanung kennt vier Arten von Abhängigkeit. Hier gibt es **eine**:
„Diese Aufgabe kann erst beginnen, wenn jene fertig ist." Das ist der Fall,
der im Haus vorkommt — die Technik richtet ein, wenn das Bühnenbild steht.
Die übrigen drei würden die Oberfläche verkomplizieren, ohne dass jemand sie
füllt.

**Der Zeitstrahl rechnet nichts um.** Wer das Bühnenbild verschiebt,
verschiebt nicht automatisch die Einrichtung; souffleur meldet nur, dass die
Reihenfolge nicht mehr aufgeht. Automatisches Umplanen ist die Stelle, an der
solche Werkzeuge unbenutzbar werden: Eine Verschiebung reißt zwanzig Termine
mit, die jemand mühsam abgestimmt hatte.

Zwei Regeln dazu:

- **Derselbe Tag ist in Ordnung.** Im Theater wird vormittags abgebaut und
  nachmittags eingerichtet. Auf den Tag genau zu rechnen hieße zu behaupten,
  man wüsste es besser.
- **Kreise werden abgelehnt.** Wartete am Ende jede Aufgabe auf sich selbst,
  könnte keine je beginnen.

Ein Pfeil wird nur gezeichnet, wenn **beide Enden** im Ausschnitt liegen — ein
Pfeil ins Nichts erklärt nichts. Die Verbindung bleibt trotzdem bestehen und
wird geprüft.

Im Vorführbestand ist eine Reihenfolge absichtlich verdreht: Der Pressetext
braucht Fotos vom fertigen Bühnenbild, ist aber vorher fällig. Der Zeitstrahl
soll zeigen, wie das aussieht — nicht nur den heilen Fall.

## Beteiligte und Verknüpfungen (Nachtrag 04.11.2026)

Die eine Zuständigkeit bleibt. Dazu kommen zwei Listen von Kontakten mit
klar verschiedener Bedeutung, in einer Tabelle (`TaskContact`, `Kind`):

- **Beteiligt** — arbeitet mit, trägt die Aufgabe aber nicht. Die Frist
  steht auch im Kalender der Beteiligten (Personenfilter, Abonnement), und
  wer neu dazukommt, bekommt die Glocke. Das ist der Fall „Regieassistenz an
  der Probenplanung": Sie soll wissen, dass es die Aufgabe gibt, ohne dass
  jemand so tut, als sei sie dafür verantwortlich.
- **Verknüpft** — kommt darin vor, tut nichts: die Spielstätte, um die es
  geht, die Agentur, mit der verhandelt wird. Kein Kalender, keine Glocke.

Dazu, im Bogen unter „Verknüpfungen" abgesetzt: die Produktion (ADR-0011),
eine **Kostenstelle** (Verknüpfung, keine Buchung — die Aufgabe kostet
nichts, sie gehört nur wohin), **Dokumente** aus der Ablage (anheften oder
hochladen, auch schon beim Anlegen — dann vorgemerkt und nach dem Speichern
verknüpft) und **Lesezeichen** (URL mit Stichwort, überall im Haus gleich).

Bewusst weiterhin keine Verknüpfung zu einer einzelnen Buchung: Eine Aufgabe
„Rechnung prüfen" hängt besser am Beleg, und der Beleg hat seine Unterhaltung
und seine Dokumente schon. Kommt der Bedarf, ist es ein Enum-Wert.

## Offen

- **Aufgaben an Gegenständen.** Eine Aufgabe zu einer Rechnung, einem Beleg,
  einer Produktion. Der Chat kann das schon (Unterhaltung an einem
  Gegenstand); die Aufgaben noch nicht.
- **Wiederkehrende Aufgaben.** „Jeden Monatsersten die Kasse zählen." Die
  Regelauswertung dafür gibt es bereits im Kalender; ob sie sich teilen lässt,
  ist noch nicht geprüft.
