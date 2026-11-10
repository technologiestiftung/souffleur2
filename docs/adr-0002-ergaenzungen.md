# Ergänzungen zu ADR-0002 aus der Umsetzung

Kurze Nachträge, die den Stack betreffen, aber kein eigenes ADR rechtfertigen.
Das Hauptdokument liegt im Repo `souffleur`.

## 30.10.2026 — Sphärentrennung: fertig, aber ruhend

Unter den Häusern, für die souffleur entstand, ist **kein Verein**. Die Vereinsvariante wird deshalb nicht weiter
ausgebaut.

**Gemeinnützige GmbHs sind aber wahrscheinlich dabei** (bestätigt am 30.10.2026).
Damit bleibt die Sphärentrennung fachlich erforderlich: Sie hängt an der
Gemeinnützigkeit, nicht an der Rechtsform. Eine gGmbH trennt genauso nach ideellem
Bereich, Vermögensverwaltung, Zweckbetrieb und wirtschaftlichem Geschäftsbetrieb
wie ein e. V.

Stand des Codes: fertig und geprüft, aber inaktiv. Er hängt an
`Organization.IsNonProfit` und am Kontenrahmen SKR42 und greift nur, wenn eines von
beiden eingeschaltet ist. Vorgabe für eine neue Installation bleibt SKR03 ohne
Sphären.

**Vor dem ersten produktiven Buchen zu klären:** Rechtsform und
Gemeinnützigkeitsstatus jedes Hauses. Beides gehört in die Stammdaten des
Hauses (Einstellungen → Haus). Ein gemeinnütziges Haus, das ohne das
Kennzeichen bucht, erzeugt einen falschen DATEV-Stapel, und zwar ohne
Fehlermeldung.
