/**
 * Die Module in der Seitenleiste. Reihenfolge und Beschriftung stammen aus dem
 * Prototyp; die Rollen entscheiden, wer was sieht.
 */
export interface ModuleEntry {
  readonly path: string;
  readonly label: string;
  readonly roles?: readonly string[];
}

export const MODULES: readonly ModuleEntry[] = [
  { path: 'uebersicht', label: 'Übersicht' },
  { path: 'ticketing', label: 'Ticketing' },
  { path: 'finanzen', label: 'Finanzen', roles: ['Administrator', 'Management', 'Accounting'] },
  { path: 'personal', label: 'Personal', roles: ['Administrator', 'Management', 'Scheduling'] },
  { path: 'ksk', label: 'KSK', roles: ['Administrator', 'Management', 'Accounting'] },
  { path: 'aufgaben', label: 'Aufgaben' },
];
