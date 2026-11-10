import { JournalEntry, CostCenter } from './api';
import { BankTransaction } from './bank.service';
import { Occurrence, CalendarSource, EventStatus } from './calendar.service';
import { Contact, RoleInfo } from './contacts.service';
import { TaskItem } from './tasks.service';
import { Performance } from './ticketing.service';
import { Tour, TourStopStatus } from './touring.service';
import { Production, ProductionStatus } from './productions.service';
import { Voucher } from './vouchers.service';

/**
 * Die Kennzahlen eines Moduls, als Daten statt als Markup.
 *
 * Hier steht nur, was gezählt wird -- gezeichnet wird in shared/. Dadurch
 * zeigt die Übersicht dieselben Kacheln wie das Modul selbst, ohne dass
 * irgendwo eine zweite Rechnung entsteht: Beide holen sie von hier.
 */

/** Eine Scheibe im Donut oder ein Balken. */
export interface MetricSegment {
  readonly label: string;
  readonly value: number;

  /**
   * Platz in der Farbreihe, 1 bis 6 -- nie darüber hinaus, siehe styles.css.
   * 0 heißt „kein eigener Platz": der zusammengefasste Rest, in Grau.
   */
  readonly slot: number;

  /** Was in der Legende neben dem Namen steht. Ohne Angabe die nackte Zahl. */
  readonly note?: string;
}

export type MetricShape = 'donut' | 'bars';

export interface MetricTile {
  readonly key: string;
  readonly label: string;
  readonly shape: MetricShape;

  /** Die Zahl in der Mitte des Donuts. */
  readonly center: string;
  readonly centerNote: string;
  readonly segments: readonly MetricSegment[];

  /** Der Satz, der erscheint, wenn es nichts zu zählen gibt. */
  readonly empty: string;
}

const euro = new Intl.NumberFormat('de-DE', {
  style: 'currency',
  currency: 'EUR',
  maximumFractionDigits: 0,
});

const SLOTS = 6;

/** Zählt und sortiert, kappt den Schwanz bei fünf und fasst den Rest zusammen. */
function ranked(counts: Map<string, number>, limit = 5): MetricSegment[] {
  const sorted = [...counts.entries()].filter(([, value]) => value > 0).sort((a, b) => b[1] - a[1]);
  const head = sorted.slice(0, limit);
  const tail = sorted.slice(limit);

  const segments: MetricSegment[] = head.map(([label, value], index) => ({
    label,
    value,
    slot: index + 1,
  }));

  if (tail.length > 0) {
    segments.push({
      label: 'Übrige',
      value: tail.reduce((sum, [, value]) => sum + value, 0),
      slot: 0,
    });
  }

  return segments;
}

/** Zählt in der vorgegebenen Reihenfolge -- für Stände, die eine haben. */
function ordered(pairs: readonly (readonly [string, number])[]): MetricSegment[] {
  return pairs
    .filter(([, value]) => value > 0)
    .map(([label, value], index) => ({ label, value, slot: (index % SLOTS) + 1 }));
}

function tally<T>(items: readonly T[], key: (item: T) => string): Map<string, number> {
  const counts = new Map<string, number>();
  for (const item of items) {
    const name = key(item);
    counts.set(name, (counts.get(name) ?? 0) + 1);
  }
  return counts;
}

function sum(segments: readonly MetricSegment[]): number {
  return segments.reduce((total, segment) => total + segment.value, 0);
}

/* ------------------------------------------------------------------ Finanzen */

export function financeTiles(
  entries: readonly JournalEntry[],
  vouchers: readonly Voucher[],
  transactions: readonly BankTransaction[],
  costCenters: readonly CostCenter[],
): MetricTile[] {
  const committed = entries.filter((e) => e.isCommitted);
  const drafts = entries.filter((e) => !e.isCommitted);

  const booked = vouchers.filter((v) => v.isBooked);
  const open = vouchers.filter((v) => !v.isBooked);

  const assigned = transactions.filter((t) => t.isAssigned);
  const unassigned = transactions.filter((t) => !t.isAssigned);

  const perCostCenter = new Map<string, number>();
  const names = new Map(costCenters.map((c) => [c.id, `${c.number} ${c.name}`]));
  for (const entry of entries) {
    for (const line of entry.lines) {
      if (line.debit <= 0) {
        continue;
      }
      const name = line.costCenterId
        ? (names.get(line.costCenterId) ?? 'Unbekannte Kostenstelle')
        : 'Ohne Kostenstelle';
      perCostCenter.set(name, (perCostCenter.get(name) ?? 0) + line.debit);
    }
  }

  const costCenterSegments = ranked(perCostCenter, 5).map((segment) => ({
    ...segment,
    note: euro.format(segment.value),
  }));

  return [
    {
      key: 'finanzen-buchungen',
      label: 'Buchungen',
      shape: 'donut',
      center: String(entries.length),
      centerNote: 'im Journal',
      empty: 'Noch nichts gebucht.',
      segments: ordered([
        ['Festgeschrieben', committed.length],
        ['Entwurf', drafts.length],
      ]).map((segment) => ({
        ...segment,
        note: `${segment.value} · ${euro.format(
          (segment.label === 'Entwurf' ? drafts : committed).reduce((s, e) => s + e.total, 0),
        )}`,
      })),
    },
    {
      key: 'finanzen-belege',
      label: 'Belege',
      shape: 'donut',
      center: String(open.length),
      centerNote: 'ohne Buchung',
      empty: 'Keine Belege erfasst.',
      segments: ordered([
        ['Gebucht', booked.length],
        ['Offen', open.length],
      ]).map((segment) => ({
        ...segment,
        note: `${segment.value} · ${euro.format(
          (segment.label === 'Offen' ? open : booked).reduce((s, v) => s + v.grossAmount, 0),
        )}`,
      })),
    },
    {
      key: 'finanzen-bank',
      label: 'Bankumsätze',
      shape: 'donut',
      center: String(unassigned.length),
      centerNote: 'offen',
      empty: 'Noch kein Auszug eingelesen.',
      segments: ordered([
        ['Zugeordnet', assigned.length],
        ['Offen', unassigned.length],
      ]),
    },
    {
      key: 'finanzen-kostenstellen',
      label: 'Soll nach Kostenstelle',
      shape: 'bars',
      center: euro.format(sum(costCenterSegments)),
      centerNote: 'im Soll',
      empty: 'Keine Sollbuchungen vorhanden.',
      segments: costCenterSegments,
    },
  ];
}

/* ------------------------------------------------------------------ Aufgaben */

export function taskTiles(tasks: readonly TaskItem[]): MetricTile[] {
  const open = tasks.filter((t) => t.isOpen);

  const inAWeek = new Date();
  inAWeek.setDate(inAWeek.getDate() + 7);
  const limit = inAWeek.toISOString().slice(0, 10);

  const overdue = open.filter((t) => t.isOverdue);
  const soon = open.filter((t) => !t.isOverdue && t.dueDate && t.dueDate.slice(0, 10) <= limit);
  const later = open.filter((t) => !t.isOverdue && t.dueDate && t.dueDate.slice(0, 10) > limit);
  const undated = open.filter((t) => !t.dueDate);

  // Der Stand kommt beschriftet vom Server; die Reihenfolge ist seine.
  const states = new Map<number, { label: string; count: number }>();
  for (const task of tasks) {
    const found = states.get(task.state);
    if (found) {
      found.count += 1;
    } else {
      states.set(task.state, { label: task.stateLabel, count: 1 });
    }
  }

  const byState = [...states.entries()].sort((a, b) => a[0] - b[0]);

  return [
    {
      key: 'aufgaben-stand',
      label: 'Aufgaben nach Stand',
      shape: 'donut',
      center: String(open.length),
      centerNote: 'offen',
      empty: 'Keine Aufgaben angelegt.',
      segments: ordered(byState.map(([, entry]) => [entry.label, entry.count] as const)),
    },
    {
      key: 'aufgaben-fristen',
      label: 'Fristen der offenen Aufgaben',
      shape: 'donut',
      center: String(overdue.length),
      centerNote: 'überfällig',
      empty: 'Nichts offen.',
      segments: ordered([
        ['Überfällig', overdue.length],
        ['Diese Woche', soon.length],
        ['Später', later.length],
        ['Ohne Frist', undated.length],
      ]),
    },
    {
      key: 'aufgaben-kategorien',
      label: 'Offene nach Kategorie',
      shape: 'donut',
      center: String(new Set(open.map((t) => t.categoryLabel ?? '—')).size),
      centerNote: 'Kategorien',
      empty: 'Nichts offen.',
      segments: ranked(tally(open, (t) => t.categoryLabel ?? 'Ohne Kategorie')),
    },
    {
      key: 'aufgaben-zustaendig',
      label: 'Offene nach Zuständigkeit',
      shape: 'bars',
      center: String(open.length),
      centerNote: 'offen',
      empty: 'Nichts offen.',
      segments: ranked(tally(open, (t) => t.assigneeName ?? 'Niemand zuständig')),
    },
  ];
}

/* ------------------------------------------------------------------ Kontakte */

export function contactTiles(
  contacts: readonly Contact[],
  roles: readonly RoleInfo[],
): MetricTile[] {
  const perRole = new Map<string, number>();
  for (const role of roles) {
    perRole.set(role.label, 0);
  }
  for (const contact of contacts) {
    for (const role of contact.roles) {
      perRole.set(role.label, (perRole.get(role.label) ?? 0) + 1);
    }
  }

  const mayBeContacted = contacts.filter((c) => c.mayBeContacted).length;
  const organizations = contacts.filter((c) => c.kind === 1).length;

  const reachable = ordered([
    ['E-Mail', contacts.filter((c) => c.email).length],
    ['Telefon', contacts.filter((c) => c.phone).length],
    ['Ort', contacts.filter((c) => c.city).length],
    ['Bankverbindung', contacts.filter((c) => c.bankDetails?.iban).length],
  ]);

  return [
    {
      key: 'kontakte-rollen',
      label: 'Kontakte nach Rolle',
      shape: 'donut',
      center: String(contacts.length),
      centerNote: 'Kontakte',
      // Ein Kontakt kann mehrere Rollen haben; die Summe der Scheiben ist
      // deshalb größer als die Zahl in der Mitte. Darum steht sie dort.
      empty: 'Noch keine Kontakte.',
      segments: ranked(perRole),
    },
    {
      key: 'kontakte-art',
      label: 'Organisation oder Person',
      shape: 'donut',
      center: String(contacts.length),
      centerNote: 'Kontakte',
      empty: 'Noch keine Kontakte.',
      segments: ordered([
        ['Organisation', organizations],
        ['Person', contacts.length - organizations],
      ]),
    },
    {
      key: 'kontakte-einwilligung',
      label: 'Ansprechbar',
      shape: 'donut',
      center: String(mayBeContacted),
      centerNote: 'mit Einwilligung',
      empty: 'Noch keine Kontakte.',
      segments: ordered([
        ['Darf angesprochen werden', mayBeContacted],
        ['Ohne Einwilligung', contacts.length - mayBeContacted],
      ]),
    },
    {
      key: 'kontakte-wege',
      label: 'Hinterlegte Angaben',
      shape: 'bars',
      center: String(contacts.length),
      centerNote: 'Kontakte',
      empty: 'Noch keine Kontakte.',
      segments: reachable.map((segment) => ({
        ...segment,
        note: `${segment.value} von ${contacts.length}`,
      })),
    },
  ];
}

/* ------------------------------------------------------------------ Kalender */

export function calendarTiles(occurrences: readonly Occurrence[], period: string): MetricTile[] {
  const perResource = new Map<string, number>();
  for (const occurrence of occurrences) {
    for (const resource of occurrence.resources) {
      perResource.set(resource.name, (perResource.get(resource.name) ?? 0) + 1);
    }
  }

  const planned = occurrences.filter((o) => o.status === EventStatus.Planned).length;
  const confirmed = occurrences.filter((o) => o.status === EventStatus.Confirmed).length;
  const cancelled = occurrences.filter((o) => o.status === EventStatus.Cancelled).length;

  return [
    {
      key: 'kalender-status',
      label: `Termine ${period}`,
      shape: 'donut',
      center: String(occurrences.length),
      centerNote: 'Termine',
      empty: 'Keine Termine in diesem Zeitraum.',
      segments: ordered([
        ['Bestätigt', confirmed],
        ['Geplant', planned],
        ['Abgesagt', cancelled],
      ]),
    },
    {
      key: 'kalender-kategorien',
      label: 'Nach Kategorie',
      shape: 'donut',
      center: String(occurrences.length),
      centerNote: 'Termine',
      empty: 'Keine Termine in diesem Zeitraum.',
      segments: ranked(tally(occurrences, (o) => o.categoryLabel ?? 'Ohne Kategorie')),
    },
    {
      key: 'kalender-herkunft',
      label: 'Woher die Einträge kommen',
      shape: 'donut',
      center: String(occurrences.filter((o) => o.source === CalendarSource.Calendar).length),
      centerNote: 'aus dem Kalender',
      empty: 'Keine Termine in diesem Zeitraum.',
      segments: ordered([
        ['Kalender', occurrences.filter((o) => o.source === CalendarSource.Calendar).length],
        ['Aufgaben', occurrences.filter((o) => o.source === CalendarSource.Tasks).length],
        ['Werke', occurrences.filter((o) => o.source === CalendarSource.Artwork).length],
        ['Ticketing', occurrences.filter((o) => o.source === CalendarSource.Ticketing).length],
      ]),
    },
    {
      key: 'kalender-ressourcen',
      label: 'Belegte Ressourcen',
      shape: 'bars',
      center: String(perResource.size),
      centerNote: 'belegt',
      empty: 'Keine Ressource belegt.',
      segments: ranked(perResource),
    },
  ];
}

/* ----------------------------------------------------------------- Ticketing */

export function ticketingTiles(performances: readonly Performance[]): MetricTile[] {
  const today = new Date().toISOString().slice(0, 10);
  const upcoming = performances.filter((p) => p.date.slice(0, 10) >= today);
  const played = performances.filter((p) => p.date.slice(0, 10) < today);

  // Nur Vorstellungen mit belastbarer Platzzahl tragen eine Auslastung. Die
  // übrigen fehlen in der Rechnung -- und werden daneben genannt, damit die
  // Zahl nicht mehr verspricht, als sie deckt.
  const measurable = performances.filter((p) => p.utilization !== null);
  const average = measurable.length
    ? measurable.reduce((sum, p) => sum + (p.utilization ?? 0), 0) / measurable.length
    : 0;

  const bands = ordered([
    ['Über 80 %', measurable.filter((p) => (p.utilization ?? 0) > 80).length],
    [
      '50 bis 80 %',
      measurable.filter((p) => {
        const value = p.utilization ?? 0;
        return value > 50 && value <= 80;
      }).length,
    ],
    ['Bis 50 %', measurable.filter((p) => (p.utilization ?? 0) <= 50).length],
    ['Ohne Platzzahl', performances.length - measurable.length],
  ]);

  const revenue = performances.reduce((sum, p) => sum + p.revenue, 0);
  const sold = performances.reduce((sum, p) => sum + p.sold, 0);

  // Nach Produktion, nicht nach Termin: Eine Wiederaufnahme steht in pretix
  // als Terminserie, und die Frage lautet „was läuft", nicht „welcher Abend".
  const perProduction = new Map<string, number>();
  for (const performance of performances) {
    perProduction.set(
      performance.name,
      (perProduction.get(performance.name) ?? 0) + performance.sold,
    );
  }

  return [
    {
      key: 'ticketing-auslastung',
      label: 'Auslastung',
      shape: 'donut',
      center: `${Math.round(average)} %`,
      centerNote: 'im Mittel',
      empty: 'Noch keine Vorstellung mit Platzzahl.',
      segments: bands,
    },
    {
      key: 'ticketing-termine',
      label: 'Vorstellungen',
      shape: 'donut',
      center: String(performances.length),
      centerNote: 'im Vorverkauf',
      empty: 'Keine Vorstellungen im Vorverkauf.',
      segments: ordered([
        ['Kommen noch', upcoming.length],
        ['Gespielt', played.length],
      ]),
    },
    {
      key: 'ticketing-karten',
      label: 'Verkaufte Karten',
      shape: 'donut',
      center: String(sold),
      centerNote: 'Karten',
      empty: 'Noch keine Karte verkauft.',
      segments: ordered([
        ['Kommende Termine', upcoming.reduce((sum, p) => sum + p.sold, 0)],
        ['Gespielte Termine', played.reduce((sum, p) => sum + p.sold, 0)],
      ]),
    },
    {
      key: 'ticketing-erloes',
      label: 'Karten je Produktion',
      shape: 'bars',
      center: euro.format(revenue),
      centerNote: 'Erlös insgesamt',
      empty: 'Noch keine Karte verkauft.',
      segments: ranked(perProduction),
    },
  ];
}

/* ------------------------------------------------------------------ Dienste */

/**
 * Die Dienste des Hauses -- wer wann zu welchem Proben- oder
 * Vorstellungsdienst eingeteilt ist.
 *
 * Nicht zu verwechseln mit der Besetzung: Die sagt, wer welche Rolle spielt,
 * und hängt an der Produktion. Hier geht es um die Einteilung am Termin.
 *
 * Gerechnet wird aus dem Kalender, weil es das Modul Personal als eigenes
 * Fachgebiet noch nicht gibt. Dienstplan, Zeiterfassung und
 * Künstlersozialabgabe kommen später und bringen eigene Zahlen mit.
 */
export function dutyTiles(occurrences: readonly Occurrence[], period: string): MetricTile[] {
  // Fristen aus den Aufgaben stehen im Kalender, sind aber keine Dienste --
  // sie tragen „Zuständig" als Funktion und würden die Zählung verfälschen.
  const dates = occurrences.filter((o) => o.source === CalendarSource.Calendar);
  const duties = dates.flatMap((o) => o.participants);

  const perStatus = new Map<number, { label: string; count: number }>();
  for (const duty of duties) {
    const found = perStatus.get(duty.status);
    if (found) {
      found.count += 1;
    } else {
      perStatus.set(duty.status, { label: duty.statusLabel, count: 1 });
    }
  }

  const manned = dates.filter((o) => o.participants.length > 0).length;

  return [
    {
      key: 'dienste-zusagen',
      label: `Dienste ${period}`,
      shape: 'donut',
      center: String(duties.length),
      centerNote: 'Dienste',
      empty: 'Niemand eingeteilt.',
      segments: ordered(
        [...perStatus.entries()]
          .sort((a, b) => a[0] - b[0])
          .map(([, entry]) => [entry.label, entry.count] as const),
      ),
    },
    {
      key: 'dienste-funktionen',
      label: 'Nach Funktion',
      shape: 'donut',
      center: String(new Set(duties.map((d) => d.function ?? '—')).size),
      centerNote: 'Funktionen',
      empty: 'Niemand eingeteilt.',
      segments: ranked(tally(duties, (d) => d.function ?? 'Ohne Funktion')),
    },
    {
      key: 'dienste-ohne-einteilung',
      label: 'Termine mit Einteilung',
      shape: 'donut',
      center: String(dates.length - manned),
      centerNote: 'noch ohne',
      empty: 'Keine Termine in diesem Zeitraum.',
      segments: ordered([
        ['Jemand eingeteilt', manned],
        ['Noch niemand', dates.length - manned],
      ]),
    },
    {
      key: 'dienste-je-person',
      label: 'Dienste je Person',
      shape: 'bars',
      center: String(duties.length),
      centerNote: 'Dienste',
      empty: 'Niemand eingeteilt.',
      segments: ranked(tally(duties, (d) => d.name)),
    },
  ];
}

/**
 * Produktionen: woran das Haus gerade arbeitet, und ob das Geld reicht.
 *
 * Gezählt wird, was läuft -- Abgespieltes ist Geschichte und verzerrte den
 * Blick auf die Spielzeit. Das Budget steht gegen die Kosten aus den Belegen:
 * eine Zahl, die der Verwaltung sagt, wo nachgefragt werden muss, bevor die
 * Buchhaltung es tut. Ohne Finanzrecht fehlen die Kosten, die Kachel bleibt
 * dann beim Plan.
 */
export function productionTiles(productions: readonly Production[]): MetricTile[] {
  const current = productions.filter((p) => p.isCurrent);

  const byStatus = ordered([
    ['Im Spielplan', current.filter((p) => p.status === ProductionStatus.Running).length],
    ['In Probe', current.filter((p) => p.status === ProductionStatus.Rehearsing).length],
    ['In Planung', current.filter((p) => p.status === ProductionStatus.Planned).length],
    ['Abgespielt', productions.filter((p) => p.status === ProductionStatus.Archived).length],
  ]);

  const budget = current.reduce((sum, p) => sum + (p.budget ?? 0), 0);
  const planned = current.reduce((sum, p) => sum + (p.figures?.planned ?? 0), 0);
  const costs = current.reduce((sum, p) => sum + (p.figures?.costs ?? 0), 0);
  const income = current.reduce((sum, p) => sum + (p.figures?.income ?? 0), 0);

  // Offene Aufgaben je Produktion: Wo brennt es?
  const openTasks = new Map<string, number>();
  for (const production of current) {
    if (production.figures && production.figures.openTasks > 0) {
      openTasks.set(production.title, production.figures.openTasks);
    }
  }

  return [
    {
      key: 'produktionen-stand',
      label: 'Produktionen nach Stand',
      shape: 'donut',
      center: String(current.length),
      centerNote: current.length === 1 ? 'laufende Produktion' : 'laufende Produktionen',
      segments: byStatus,
      empty: 'Noch keine Produktion angelegt.',
    },
    {
      key: 'produktionen-budget',
      label: 'Budget, Plan und Kosten',
      shape: 'bars',
      center: euro.format(costs),
      centerNote: budget > 0 ? `von ${euro.format(budget)} geplant` : 'Kosten aus Belegen',
      segments: ordered([
        ['Budget', budget],
        ['Verplant', planned],
        ['Kosten', costs],
        ['Einnahmen', income],
      ]).map((segment) => ({ ...segment, note: euro.format(segment.value) })),
      empty: 'Noch kein Budget und kein Beleg an einer Produktion.',
    },
    {
      key: 'produktionen-aufgaben',
      label: 'Offene Aufgaben je Produktion',
      shape: 'bars',
      center: String([...openTasks.values()].reduce((sum, n) => sum + n, 0)),
      centerNote: 'offen',
      segments: ranked(openTasks),
      empty: 'Keine offene Aufgabe hängt an einer Produktion.',
    },
  ];
}

/**
 * Tourneen: wie weit die Gastspiele sind, und was an Gagen zugesagt ist.
 *
 * Nach Gastspiel gezählt, nicht nach Tournee: Eine Tournee mit sechs Orten ist
 * sechs Verhandlungen, und die Frage lautet „wie viele Abende sind fest?",
 * nicht „wie viele Reisen". Abgesagtes zählt mit -- als eigene Scheibe, damit
 * sichtbar bleibt, wie viel vom Angebotenen nicht zustande kam.
 */
export function touringTiles(tours: readonly Tour[]): MetricTile[] {
  const stops = tours.flatMap((t) => t.stops);
  const binding = stops.filter((s) => s.isBinding);
  const performances = binding.reduce((sum, s) => sum + s.performances.length, 0);

  const byStatus = ordered([
    ['Bestätigt', stops.filter((s) => s.status === TourStopStatus.Confirmed).length],
    ['Angeboten', stops.filter((s) => s.status === TourStopStatus.Offered).length],
    ['Anfrage', stops.filter((s) => s.status === TourStopStatus.Inquiry).length],
    ['Abgerechnet', stops.filter((s) => s.status === TourStopStatus.Settled).length],
    ['Abgesagt', stops.filter((s) => s.status === TourStopStatus.Cancelled).length],
  ]);

  // Gagen: nur was zugesagt ist, zählt als Einnahme; Angebotenes steht
  // daneben als das, was noch offen ist. Gastspiele ohne Gage fehlen in beiden
  // Summen -- die Zahl verspricht nicht mehr, als eingetragen ist.
  const confirmedFee = binding.reduce((sum, s) => sum + (s.fee ?? 0), 0);
  const openFee = stops
    .filter((s) => s.status === TourStopStatus.Offered || s.status === TourStopStatus.Inquiry)
    .reduce((sum, s) => sum + (s.fee ?? 0), 0);
  const invoiced = binding.filter((s) => s.voucherId).reduce((sum, s) => sum + (s.fee ?? 0), 0);

  // Nach Spielstätte: wer lädt wie oft ein?
  const perVenue = new Map<string, number>();
  for (const stop of binding) {
    perVenue.set(stop.venueName, (perVenue.get(stop.venueName) ?? 0) + 1);
  }

  return [
    {
      key: 'touring-stand',
      label: 'Gastspiele nach Stand',
      shape: 'donut',
      center: String(binding.length),
      centerNote: binding.length === 1 ? 'fester Abend' : 'feste Gastspiele',
      segments: byStatus,
      empty: 'Noch keine Gastspiele angelegt.',
    },
    {
      key: 'touring-gagen',
      label: 'Gagen',
      shape: 'bars',
      center: euro.format(confirmedFee),
      centerNote: 'zugesagt',
      segments: ordered([
        ['Zugesagt', confirmedFee],
        ['In Rechnung gestellt', invoiced],
        ['Noch offen (Angebote, Anfragen)', openFee],
      ]).map((segment) => ({ ...segment, note: euro.format(segment.value) })),
      empty: 'Noch keine Gage eingetragen.',
    },
    {
      key: 'touring-orte',
      label: 'Spielstätten',
      shape: 'bars',
      center: String(performances),
      centerNote: performances === 1 ? 'Vorstellung auf Reisen' : 'Vorstellungen auf Reisen',
      segments: ranked(perVenue),
      empty: 'Noch kein festes Gastspiel.',
    },
  ];
}
