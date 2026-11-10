# Bankanbindung: was souffleur 2 aus ARPA übernimmt

**Stand:** 30.10.2026 · **Quelle:** ein anderes Projekt mit FinTS-Anbindung, `lib/libfintx`, Commits vom 28. und 29.10.2026
**Zweck:** Diese Arbeit nicht zweimal machen

souffleur 2 bindet die Bank über FinTS an (ADR-0002, T9/T10). Die Bibliothek dafür ist
`libfintx`, einvendort unter `lib/libfintx` — aber **nicht in der Fassung von Upstream**.
In ARPA stecken zwölf Korrekturen aus zwei Tagen, ohne die Überweisungen und
Lastschriften bei echten Banken scheitern.

Dieses Papier hält fest, was dabei gelernt wurde. Es ist keine Anleitung zum
Nachprogrammieren, sondern die Begründung, warum der ARPA-Stand zu übernehmen ist
und nicht der von Upstream.

---

## Der Stand

Produktiv erprobt mit **Volksbanken (Atruvia)** und **Sparkassen**, jeweils
Überweisung und Lastschrift:

- eine Volksbank — sieben Ein-Euro-Überweisungen am 28. und 29.10.2026, an denen
  die Fixes unten entstanden sind
- eine Sparkasse — das zweite Rechenzentrum, an dem sich zeigte,
  dass die Unterschiede unten keine Atruvia-Eigenheiten sind

Eine Registrierung bei der Deutschen Kreditwirtschaft war nicht nötig.

---

## Das eigentliche Learning

**Zwei deutsche Rechenzentren, zwei verschiedene Protokollwege — und beide sind
konform.** Wer gegen eine Bank entwickelt, baut die andere kaputt. Die Lösung ist,
sich konsequent nach den Bankparameterdaten (BPD, HITANS) zu richten statt ein
Verhalten fest zu verdrahten.

| Stelle | Atruvia (Volksbanken) | Sparkassen |
|---|---|---|
| Anonyme BPD-Abfrage | gelingt | scheitert |
| TAN-Verfahren | 946 | 923 |
| TAN-Medium im HKTAN | HITANS Feld 19 = 0 → **weglassen** | = 2 → **mitsenden** |
| Auftraggeberkonto im HKTAN | HITANS Feld 15 = 2 → **mitsenden** | = 0 → weglassen |
| VoP-Ablauf | Rückmeldung ohne 3093 → nur `HKVPP(Polling-ID)` | mit 3093 → kompletter Resubmit |
| Leeres `<BIC></BIC>` im pain | bricht ab (3070 + 3999) | wurde toleriert (Stand 07/2026) |

Die rechte Spalte ist der Grund, warum es überhaupt so lange lief: Die Sparkasse
verzieh einen Fehler, den Atruvia nicht verzeiht.

---

## Die vier Fallen, einzeln

### 1. Schemaverletzungen fallen erst bei der Freigabe auf

Ein leeres `<BIC></BIC>` entsteht, sobald der Empfänger nur per IBAN bekannt ist.
Das verletzt das BIC-Muster des pain-Schemas. Die Bank nimmt den Auftrag an und
bricht **erst bei der Challenge-Erzeugung** ab — mit 3070 „Ungültige
Challenge-Anforderung" und 3999 „technisches Problem". Die Fehlermeldung zeigt
nicht auf die Ursache.

Richtig ist die Konvention der Deutschen Kreditwirtschaft:
`<Othr><Id>NOTPROVIDED</Id></Othr>` statt eines leeren Elements.

**Für souffleur besonders wichtig:** pretix-Exporte liefern oft keine BIC. Wer
Erstattungen oder Lastschriften aus Ticketdaten erzeugt, läuft direkt hinein.

### 2. Ein Erfolgscode schlägt keinen Fehlercode

Eine FinTS-Antwort kann gleichzeitig 0020 „Ausführungsbestätigung" und 9050 / 9260 /
3070 enthalten. Wer auf „irgendein 0xxx" prüft, meldet dem Haus einen Erfolg, den es
nicht gab. Fehlercodes müssen Vorrang haben.

### 3. Doppelte Systemkennung zerlegt den Dialog

Lag eine Kundensystem-ID bereits vor und wurde trotzdem ein zweites `HKSYN`
geschickt, gab die Bank eine **neue** ID zurück. Die folgende Nachricht trug dann
eine andere Kennung als die Dialogeröffnung — Ergebnis: 9050 / 9800 / 9010 „Die
angegebene Bankreferenz ist nicht gültig".

### 4. VoP ist neu und uneinheitlich

Die Empfängerprüfung (Verification of Payee) ist frische EU-Pflicht, und die
Rechenzentren setzen sie verschieden um. Bei Atruvia erzeugt der
Sparkassen-Resubmit jedes Mal einen **neuen** VoP-Auftrag — sechzigmal Fehler 3945,
dann Zeitüberschreitung, nie eine Freigabe im TAN-Gerät. Dort darf nur
`HKVPP(Polling-ID)` nachgefragt werden, mit dem Aufsetzpunkt aus Rückmeldung 3040.

---

## Wie es gefunden wurde

Nicht durch Lesen der Spezifikation, sondern durch **Vergleich mit fremden
Umsetzungen**: `phpFinTS` (Aufzeichnung einer echten Atruvia-Sitzung), `hbci4java`
und `python-fints`. Jeder der sieben Tests verglich den eigenen Mitschnitt mit dem
fremden und suchte den nächsten Unterschied.

Das ist der Grund, warum diese Arbeit in keiner Sprache „einfach neu geschrieben"
werden kann (vgl. ADR-0002, T11): Der Engpass war nie das Tippen, sondern der
Zyklus aus echtem Konto, echtem TAN-Gerät und einer Bank, die auf Fehler
unspezifisch antwortet.

---

## Was souffleur 2 zu tun hat

1. **`lib/libfintx` aus ARPA übernehmen**, nicht von Upstream. Die Commits vom
   28./29.10.2026 sind nicht eingeflossen.
2. **Die Testfälle mitnehmen**: `VopResultAtruviaTests`, `HktanTanMediumTests`,
   `PainBuilderTests`. Sie halten genau die Unterschiede oben fest.
3. **Beim Einvendorieren vermerken**, dass es ein Fork ist — sonst überschreibt die
   nächste Aktualisierung die Korrekturen.
4. **Vor dem ersten echten Zahllauf** mit einem Euro testen, bei der Bank des Hauses.
   Erprobt sind Volksbank und Sparkasse; ein neues Haus kann ein drittes
   Rechenzentrum mitbringen.

## Was inzwischen steht (31.10.2026)

Gebaut ist der Teil, der **ohne Bank, ohne Konto und ohne TAN-Gerät** trägt — und
der sich deshalb auch prüfen lässt:

- **Bankkonten** des Hauses, mit IBAN-Prüfziffer nach ISO 13616. Ein Zahlendreher
  fällt sonst erst auf, wenn das Geld woanders ist.
- **Kontoauszug einlesen (CAMT.053).** Gelesen wird über die lokalen Elementnamen,
  nicht über den Namensraum: Den gibt es in einem halben Dutzend Fassungen, und die
  Banken liefern verschiedene. Übernommen wird nur, was gebucht ist (`BOOK`);
  Vorgemerktes ändert sich noch.
- **Derselbe Auszug zweimal eingelesen verdoppelt nichts.** Erkannt über die
  Bankreferenz, und wo die fehlt, über einen Fingerabdruck aus Datum, Betrag,
  Zweck und Gegenseite. In der Praxis weiß niemand, welche Datei schon dran war.
- **Abgleich gegen offene Belege.** Vorgeschlagen wird nach gleichem Betrag, Nähe
  im Datum, Rechnungsnummer im Verwendungszweck und IBAN des Lieferanten — mit
  Angabe des Grundes. Entschieden wird von Hand: Zwei Rechnungen über 89 Euro im
  selben Monat sind nichts Besonderes.
- **Der Saldo kommt von der Bank**, nicht aus einer Summe. Ein Auszug, der nicht
  beim ersten Umsatz beginnt, ließe sich nicht aufaddieren.

Was **fehlt**, ist die Verbindung selbst: Umsätze abholen und Zahlläufe auslösen.
Daran ändert sich nichts ohne die vier Punkte oben unter „Was souffleur 2 zu tun
hat" — der Fork, die Testfälle, der Vermerk, und ein Euro bei der Bank des Hauses.

Die Oberfläche sagt das auch. Unter *Finanzen › Bank* steht, was fehlt und warum,
statt dass dort etwas so aussieht, als wäre es angeschlossen.

### Zum Ausprobieren

`docs/beispiele/kontoauszug-2026-03.xml` ist ein Auszug, wie eine Bank ihn
liefert, passend zum Vorführbestand. Er enthält die Fälle, an denen sich das
Einlesen beweisen muss: einen Umsatz, der seinen Beleg über alle vier Kriterien
findet (147 Punkte), einen, bei dem nur Betrag und Datum passen (78), vier ohne
Gegenstück und einen vorgemerkten, der nicht übernommen wird.

Die Konten darin gehören niemandem — die Bankleitzahl 99999999 ist nicht
vergeben, die Prüfziffern stimmen trotzdem. Das ist Absicht: Eine Muster-IBAN
aus dem Netz trägt fast immer die Leitzahl einer echten Bank.

## Offen

- Ob weitere Rechenzentren (Postbank, ING, DKB) eigene Abweichungen haben, ist
  ungeprüft. Die BPD-gesteuerte Bauweise sollte sie tragen, belegt ist es nicht.
- Die Übernahme selbst steht noch aus; ADR-0002 § 8 Nr. 3 ist damit beantwortet,
  aber nicht erledigt.
