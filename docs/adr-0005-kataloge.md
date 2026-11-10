# ADR-0005: Jede Liste, aus der ausgewählt wird, gehört dem Haus

**Stand:** 30.10.2026 · **Status:** entschieden · **Gilt für:** souffleur 2

## Entscheidung

Wo in souffleur etwas aus einer Liste ausgewählt wird — Terminkategorie, Aufgabenart,
Kostenart, Schlagwort an einem Kontakt —, ist diese Liste **Datenbestand des Hauses**
und keine Konstante im Code. Der Administrator legt Einträge an, benennt sie um,
sortiert sie, gibt ihnen eine Farbe und schaltet sie ab.

Technisch tragen alle diese Listen dieselbe Tabelle: `CatalogEntries`, unterschieden
durch `Catalog`. Eine neue Liste ist ein Wert mehr in einem Enum, kein neues Modell,
keine neue Tabelle, keine neue Oberfläche.

## Warum

Kulturbetriebe sortieren ihre Arbeit nicht gleich. Was im einen Haus „Probe,
Hauptprobe, Generalprobe, Vorstellung" heißt, heißt im anderen „WA-Probe, Endprobe,
Premiere, Repertoire". Ein Kollektiv ohne eigenes Haus braucht „Gastspiel" und
„Anreise", ein Stadttheater braucht sie nie.

Jede dieser Listen fest zu verdrahten hieße: Jede Umbenennung ist eine neue Version.
Das ist bei einem Produkt, das bei jedem Haus auf einer eigenen Maschine läuft
(ADR-0003), der sicherste Weg zu Installationen, die auseinanderlaufen.

## Was *nicht* in einen Katalog gehört

Die Trennung verläuft nicht zwischen „wichtig" und „unwichtig", sondern danach, ob
die Liste über ihre Benennung hinaus **Verhalten trägt**:

| | Beispiel | Warum dort |
|---|---|---|
| **Katalog** | Terminkategorie, Aufgabenart, Kostenart | trägt nur Name, Farbe, Reihenfolge |
| **Eigene Entität** | Sachkonto, Kostenstelle, Ressource | trägt Nummer, Kontoart, Sphäre, oder: exklusiv oder teilbar — Buchhaltung und Belegungsprüfung rechnen damit |
| **Enum im Code** | steuerliche Sphäre, Kontenrahmen, Rolle | steht im Gesetz oder im Rechtemodell; ein Haus darf sie nicht erfinden |

Sachkonten und Kostenstellen sind damit *nicht* weniger frei: Beide legt das Haus
selbst an (`/api/accounting/accounts`, `/api/accounting/cost-centers`). Sie haben nur
eine eigene Tabelle, weil sie Felder tragen, die der Katalog nicht kennt.

Die vier steuerlichen Sphären dagegen stehen in §§ 14, 65 ff. AO. Ein Haus, das eine
fünfte anlegen könnte, bekäme einen Jahresabschluss, den das Finanzamt nicht annimmt.

## Löschen gibt es nicht

Ein Katalogeintrag, auf den irgendwo verwiesen wird, lässt sich nicht löschen, nur
**abschalten**. Abgeschaltet heißt: nicht mehr auswählbar, aber an allem weiterhin
lesbar, was ihn schon trägt.

Der Grund ist derselbe wie beim Storno in der Buchhaltung: Ein gelöschter Eintrag
macht vergangene Vorgänge unlesbar. Ein Termin aus 2023, dessen Kategorie verschwunden
ist, lässt sich nicht mehr einordnen — und niemand kann rekonstruieren, was sie hieß.

Einträge, auf die noch nichts verweist, lassen sich löschen. Der Server prüft das,
nicht die Oberfläche.

## Felder

| Feld | Wofür |
|---|---|
| `Catalog` | zu welcher Liste der Eintrag gehört |
| `Key` | stabiler technischer Schlüssel, innerhalb der Liste eindeutig |
| `Label` | was in der Oberfläche steht |
| `Code` | Kurzzeichen oder Nummer, wo das Haus eines führt |
| `Color` | für Kalenderraster und Listen |
| `SortOrder` | die Reihenfolge des Hauses, nicht das Alphabet |
| `IsActive` | abgeschaltet statt gelöscht |
| `IsSystem` | mitgeliefert; umbenennbar, aber nicht löschbar |
| `ParentId` | Untergliederung, wo das Haus eine braucht (Kostenarten) |

`Key` und `Id` stehen nebeneinander, weil beide etwas anderes können: Die `Id`
verbindet Datensätze, der `Key` erlaubt dem Code und den Beispieldaten, einen
bestimmten Eintrag zu meinen, ohne seine `Id` zu kennen.

## Was mitgeliefert wird

Eine neue Installation bekommt gefüllte Listen, keine leeren — ein leeres Auswahlfeld
beim ersten Termin ist eine Hürde, kein Freiheitsgewinn. Die mitgelieferten Einträge
sind `IsSystem`: umbenennbar und abschaltbar, aber nicht löschbar, damit eine
Installation nicht ohne jede Kategorie dasteht.

Vorschlag für Terminkategorien: Probe · Hauptprobe · Generalprobe · Vorstellung ·
Premiere · Gastspiel · Veranstaltung · Sitzung · Frist · Sperrtermin.

Für Ressourcenarten: Bühne · Probebühne · Werkstatt · Raum · Fahrzeug · Technik.
Die Ressourcen selbst werden nicht mitgeliefert — welche Bühnen ein Haus hat,
weiß nur das Haus (siehe ADR-0006).
