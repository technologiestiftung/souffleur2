# ADR-0010: Touring — Tournee als Klammer, Gastspiel als Ort, alles andere geliehen

**Stand:** 04.11.2026 · **Status:** entschieden · **Gilt für:** souffleur 2

## Entscheidung

Touring ist ein eigenes, abschaltbares Modul (ADR-0004) mit zwei Ebenen:

1. **Die Tournee** ist die Klammer: ein Titel, eine Produktion aus dem Katalog der
   Produktionen, die Reisenden. Was für alle Orte gilt, steht hier einmal.
2. **Das Gastspiel** ist der Ort: Spielstätte und Ansprechperson aus den Kontakten,
   Anreise und Abreise, Vorstellungen, Gage, wer Reise und Hotel trägt, der Stand der
   Verhandlung (Anfrage → Angeboten → Bestätigt / Abgesagt → Abgerechnet).

Touring führt **nichts ein zweites Mal**, was es anderswo schon gibt:

| Was | Woher | Wie |
|---|---|---|
| Spielstätte, Ansprechperson | Kontakte | Kontakt mit Rolle „Spielstätte“; der Dienst weist andere ab |
| Vorstellungen, Reise | Kalender | als Quelle `Touring` gelesen (ADR-0006): Reise als Balken, Vorstellung zur Zeit |
| Reisende | Personal | Beteiligte mit Funktion aus dem Katalog „Funktionen am Termin“, wie an jedem Termin |
| Gage | Finanzen | Zahl am Gastspiel; der Beleg der Gagenrechnung wird verknüpft, nur Ausgangsbelege |
| Vertrag, Rider, Hotel | Dokumente | neues Ziel `TourStop` an der Dokumentablage |
| Produktion | Kataloge | neue Liste „Produktionen“ (ADR-0005), nicht mitgeliefert |

## Warum zwei Ebenen

Eine Ebene wäre einfacher — und falsch für beide Seiten, die damit arbeiten. Das
tourende Kollektiv denkt in Reisen: „die Herbsttournee mit X“ hat ein Ensemble und
einen Ablauf. Die Buchhaltung denkt in Gastgebern: „die Gage aus Hamburg“ hat einen
Beleg und einen Zahlungseingang. Beides in einen Datensatz zu pressen hieße, entweder
das Ensemble an jedem Ort neu einzutragen oder die Gagen zu summieren, bevor sie
einzeln da sind.

Eine Tournee mit einem Gastspiel ist erlaubt und der häufige Fall. Die Klammer kostet
dann einen Titel.

Drei Ebenen (Tournee → Gastspiel → Vorstellung) gibt es trotzdem, aber die dritte ist
keine eigene Sache mit Stand und Gage, sondern nur ein Zeitpunkt: Drei Abende in
Hamburg sind drei Einträge im Kalender und eine Verhandlung.

## Warum geliehen statt gebaut

Der Preis eines Moduls ist nicht sein Code, sondern das, was es doppelt führt. Ein
Touring mit eigener Spielstättenliste hätte nach einem Jahr dreißig Adressen, von
denen zehn auch im Adressbuch stehen und vier davon anders. Deshalb:

- **Spielstätte = Kontakt mit Rolle.** Die Rolle „Spielstätte“ gab es im Adressbuch
  schon, mit genau dieser Bestimmung. Der Dienst weist einen Kontakt ohne diese Rolle
  ab, statt sie stillschweigend anzunehmen — wer die Druckerei wählt, hat sich vertan.
- **Kalender liest, kopiert nicht.** Wie bei den Fristen der Aufgaben entsteht im
  Kalender kein Datensatz. Ist Touring aus, erscheint nichts; ein abgesagtes Gastspiel
  erscheint nie; eine Anfrage erscheint als „vorgemerkt“, damit niemand denselben Abend
  zweimal verspricht. Die Farbe kommt aus der mitgelieferten Terminkategorie „Gastspiel“.
- **Reisende sind Beteiligte.** Dasselbe Muster wie `EventParticipant`: Kontakt,
  Funktion, Zusage. Der Personenfilter im Kalender und der Dienstplan sehen damit, wer
  unterwegs ist, ohne Touring zu kennen.
- **Gage ist eine Zahl, der Beleg ein Verweis.** Touring schreibt keine Rechnung und
  bucht nicht. Es merkt sich, was vereinbart ist, und zeigt, ob dazu schon ein
  Ausgangsbeleg existiert. Eine Eingangsrechnung als Gage weist der Dienst ab.

## Was Touring nicht ist

- **Kein Produktionsmodul.** Die Produktion ist ein Katalogeintrag mit Namen. Besetzung,
  Budget und Probenplan einer Produktion sind eine andere Frage und ein anderes ADR.
- **Kein Reisekostenmodul.** Ob der Gastgeber Reise und Hotel trägt, steht als zwei
  Häkchen am Gastspiel. Belege dafür sind Belege in den Finanzen.
- **Keine Angebotsmappe.** Der Stand „Angeboten“ sagt, dass ein Angebot raus ist; das
  Angebot selbst ist ein Dokument am Gastspiel.

## Rechte und Vorgabe

Lesen darf, wer angemeldet ist — wer mitreist, will wissen, wann es losgeht. Anlegen,
ändern und abrechnen darf, wer das Recht **Tourneen planen** (`TouringManage`) hat:
mitgeliefert bei Administration, Geschäftsführung und Disposition.

Touring ist in einer neuen Installation **aus** (Ergänzung zu ADR-0004): Es setzt eine
Betriebsform voraus, nicht nur einen Zugang.

## Umstoßkriterium

Wenn Häuser Gastspiele brauchen, die zu keiner Tournee gehören — und dafür jedes Mal
eine Klammer mit einem Element anlegen müssen, die sie stört —, wird die Tournee
optional. Wenn die Reisenden je Ort regelmäßig verschieden sind, wandern sie vom
Gastspiel-Zusatz zur eigenen Liste je Gastspiel.
