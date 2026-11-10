# ADR-0011: Produktion — die Klammer, in der das Haus denkt

**Stand:** 04.11.2026 (nachgezogen am selben Tag) · **Status:** entschieden · **Gilt für:** souffleur 2

## Entscheidung

Die Produktion wird ein eigenes, abschaltbares Modul (ADR-0004) und ein eigener
Gegenstand: ein Stück, ein Projekt, ein Abend — mit dem Titel, wie er im Spielplan
steht, Untertitel, Stand (in Planung → in Probe → im Spielplan → abgespielt), Premiere
und Derniere, Budget, Beschreibung, Recherche und Notizen.

Die Produktion trägt zweierlei. **Erstens ihre Vorgaben**, die nirgends sonst
hingehören:

| Vorgabe | Gegenstand | Warum hier |
|---|---|---|
| Welche Rollen sind zu besetzen | `ProductionRole`: Name, Bereich (Katalog „Funktionen am Termin“), Kontakt oder offen | Eine unbesetzte Rolle ist kein Kontakt; die offene Rolle ist eine Information, keine Lücke |
| Was darf sie wofür kosten | `ProductionBudgetLine`: Bezeichnung, Bereich, Betrag; Summe gegen das Budget | Eine Budgetposition ist kein Beleg — Plan, nicht Ist |
| Was wird gebraucht | `ProductionNeed` (Art Material): Bezeichnung, Menge, abgehakt | Was beschafft werden muss, steht hier; wer es beschafft, in einer Aufgabe |
| Welche Papiere braucht es | `ProductionNeed` (Art Dokument): Bezeichnung, abgehakt | Die Liste der nötigen Papiere ist Vorgabe; die Dateien liegen in der Ablage |
| Linksammlung | Lesezeichen (`BookmarkTarget.Production`) | Textfassung, Verlag, Vorbilder, Kritiken — derselbe Baustein wie an jedem Gegenstand |

**Zweitens ist sie die Klammer**, an der die anderen Module zusammenlaufen: Jedes
trägt einen Verweis auf die Produktion, und das Produktionsbuch liest ihn.

| Was | Woher | Wie |
|---|---|---|
| Aufgaben | Aufgaben | `TaskItem.ProductionId`; Auswahl im Bogen der Aufgabe |
| Proben, Vorstellungen | Kalender | `CalendarEvent.ProductionId`, `EventSeries.ProductionId`; Auswahl im Termindialog |
| Kosten, Einnahmen | Finanzen | `Voucher.ProductionId`; Eingang zählt als Kosten, Ausgang als Einnahme, brutto |
| Textbuch, Entwürfe, Bescheide | Dokumente | neues Ziel `Production` an der Dokumentablage |
| Besetzung | Kontakte | `ProductionRole.ContactId`; vom Kontaktbogen aus lässt sich jemand in eine Produktion besetzen (`POST /api/productions/{id}/roles`) |
| Tourneen | Touring | `Tour.ProductionId` zeigt jetzt auf die Produktion statt auf einen Katalogeintrag |
| Unterhaltung | Chat | `ConversationSubject.Production` gab es schon |

## Warum ein Gegenstand und kein Katalog mehr

Bis hierher war die Produktion ein Katalogeintrag (ADR-0005), weil Touring nur einen
Namen brauchte, den Tourneen teilen. Die Häuser fragen aber nach dem, was ein Name nicht
hat: Was hat Hamlet gekostet, was steht noch an, wer gehört dazu, wann ist Premiere.
Sobald Budget, Stand und Beteiligte dazukommen, ist es eine Liste von Dingen und keine
Liste von Namen. Ein Katalog mit Budgetspalte wäre ein zweites Modell in der falschen
Tabelle.

Die Migration trägt die Einträge der Liste „Produktionen“ **mit derselben Id** in die
neue Tabelle, damit bestehende Tourneen weiter stimmen; aktiv wird „im Spielplan“,
abgeschaltet wird „abgespielt“. Danach verschwindet die Liste aus dem Katalog. Die
Nummer 7 bleibt in `CatalogKind` frei.

## Warum Verweise statt Kopien — und wo die Grenze liegt

Der Preis eines Moduls ist, was es doppelt führt (ADR-0010). Eine Produktion mit eigener
Aufgabenliste hätte nach einer Spielzeit dieselbe Aufgabe zweimal, einmal erledigt und
einmal nicht. Deshalb bleibt die Aufgabe eine Aufgabe, der Beleg ein Beleg, der Termin
ein Termin — und jeder weiß, zu welcher Produktion er gehört. Fällt die Produktion weg,
verlieren sie nur ihre Klammer (`SetNull`), nicht sich selbst.

Die Grenze: Was es **nur** für die Produktion gibt, führt die Produktion selbst. Eine
offene Rolle, eine Budgetposition, ein Bedarf, ein fehlendes Papier existieren in
keinem anderen Modul — sie würden dort zu Hilfskonstruktionen (ein Kontakt „N.N.“, ein
Beleg ohne Beleg). Die erste Fassung dieses ADR hatte die Produktion zu dünn gedacht:
nur Klammer, keine Vorgaben. Die Häuser planen aber in der Produktion, bevor es
irgendwo anders etwas gibt.

Die Verweise werden dort gesetzt, wo die Dinge entstehen — und sie entstehen auch im
Buch: Jeder Abschnitt hat ein Plus, das an Ort und Stelle anlegt, was dorthin gehört.
Was dabei entsteht, ist trotzdem der Gegenstand seines Moduls mit gesetzter
Produktion: Der Termin kommt aus dem Kalenderdialog, das Dokument geht in die Ablage
und wird verknüpft, der Beleg ist ein gewöhnlicher Beleg (Lieferant, Fälligkeit und
Buchung folgen unter Finanzen), die Aufgabe eine Aufgabe. Die Überschrift des
Abschnitts führt ins Modul.

Die Vorgaben werden **abschnittsweise** gespeichert (`PUT …/roles`, `…/budget`,
`…/needs`): Wer die Besetzung pflegt, überschreibt nicht nebenbei das
Budget, das eine Kollegin gerade ändert.

## Das Produktionsbuch

Die Oberfläche ist kein Verzeichnis, sondern ein Buch zum Blättern: links das Register
(eine Zeile je Produktion), rechts eine Produktion mit allen Abschnitten auf einen
Blick; Pfeiltasten und Pfeilknöpfe blättern, die Adresse (`?produktion=`) folgt.
Die Linksammlung hatte zuerst eine eigene Tabelle; seit es Lesezeichen an jedem
Gegenstand gibt, nutzt die Produktion dieselben (Migration `ProduktionLesezeichen`).
Jede Zeile des Registers und der Kopf des Buchs tragen den **Herzschlag**: je Woche,
was an der Produktion passiert ist — Aufgaben angelegt, geändert oder erledigt, Termine
stattgefunden, Belege datiert, Dokumente angeheftet; 26 Wochen bis zur laufenden,
klein als Linie, groß als Säulen nach Art. Zählungen, keine Beträge, deshalb für alle
sichtbar. So sieht man im Register, welche Produktion lebt und welche liegt.

## Warum der Beleg eine Ausnahme ist

Ein Beleg ist nach dem Festschreiben unveränderlich (GoBD). Zu welcher Produktion er
gehört, ist aber keine Belegangabe, sondern die Ordnung des Hauses — und die wird oft
erst nachgetragen, wenn der Beleg längst gebucht ist. `AuditBehaviour` lässt deshalb
an einem festgeschriebenen Beleg genau diese eine Änderung zu; alles andere bleibt
gesperrt. Der eigene Endpunkt (`PUT /api/productions/{id}/vouchers/{voucherId}`)
verlangt dafür beides: Produktionen verwalten und buchen dürfen.

## Was die Produktion nicht ist

- **Keine Buchhaltung.** Kosten und Einnahmen sind Bruttosummen der zugeordneten
  Belege, nicht das Journal. Die Antwort auf „was hat Hamlet bisher gekostet“, nicht auf
  „wie ist das kontiert“. Wer Produktionskosten nach Kostenarten braucht, braucht
  Kostenstellen (ADR offen).
- **Keine Besetzungsliste je Vorstellung.** Die Rollen sind der Besetzungszettel der
  Produktion; wer am Dienstag kommt, steht am Termin.
- **Kein Spielplan.** Die Termine liegen im Kalender und tragen nur den Verweis.

## Löschen und Abspielen

Gelöscht wird nur, woran nichts hängt. Hängt ein Beleg, eine Aufgabe, ein Termin, ein
Dokument oder eine Tournee daran, wird die Produktion **abgespielt** und bleibt stehen —
der Förderbescheid braucht seine Klammer noch Jahre später. Abgespieltes fehlt in den
Auswahlfeldern der anderen Module und in der Liste, solange niemand danach fragt.

## Rechte und Vorgabe

Lesen darf, wer angemeldet ist. Anlegen, ändern, besetzen und abspielen darf, wer
**Produktionen verwalten** (`ProductionsManage`, 900) hat: mitgeliefert bei
Administration, Geschäftsführung und Disposition. Die Finanzzahlen an der Produktion
folgen dem Finanzrecht, nicht dem Produktionsrecht: Ohne `FinanceRead` fehlen Kosten,
Einnahmen und Belege; Budget und Positionen bleiben sichtbar, sie sind Plan und kein
Beleg. Die Dokumente folgen der Sichtbarkeit der Ablage.

Das Modul ist in einer neuen Installation **an**: Ein Theater ohne Produktionen gibt
es nicht.

## Umstoßkriterium

Wenn Häuser die Produktionskosten nach Kostenarten brauchen und dafür anfangen, je
Produktion eine Kostenstelle anzulegen, bekommt die Produktion einen Verweis auf ihre
Kostenstelle und liest die Ist-Zahlen aus dem Journal statt aus den Belegen; dann
können auch Belege einer Budgetposition zugeordnet werden. Wenn die Besetzung je
Vorstellung regelmäßig abweicht, wandert sie an den Termin.
