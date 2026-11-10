import { IconName } from '../../shared/icon.component';

export interface Bereich {
  label: string;
  icon: IconName;
  path: string;
}

/**
 * Die Bereiche der Einstellungen, auf jeder Einstellungsseite dieselben.
 *
 * Eine Liste, nicht eine je Seite: Wer auf „Module" steht, soll sehen, dass
 * es auch Wertelisten und Ressourcen gibt -- und umgekehrt. Vorher führte
 * jede Seite ihre eigene Auswahl, und von den Wertelisten aus war außer den
 * Modulen nichts zu finden.
 *
 * Zugänge nur für die, die sie verwalten dürfen. Die Seite selbst ist am
 * Server gesperrt; hier geht es darum, niemanden auf eine Tür zu schicken,
 * die zu ist.
 */
export function einstellungsBereiche(darfZugaenge: boolean): Bereich[] {
  return [
    { label: 'Haus', icon: 'haus', path: '/einstellungen/haus' },
    { label: 'Module', icon: 'einstellungen', path: '/einstellungen/module' },
    { label: 'Kontenplan', icon: 'buchung', path: '/einstellungen/kontenplan' },
    { label: 'Wertelisten', icon: 'liste', path: '/einstellungen/listen' },
    { label: 'Ressourcen', icon: 'ressource', path: '/einstellungen/ressourcen' },
    { label: 'Bedarf je Terminart', icon: 'bedarf', path: '/einstellungen/dienstbedarf' },
    { label: 'Ordner der Ablage', icon: 'dokumente', path: '/einstellungen/ordner' },
    { label: 'Freie Tage', icon: 'kalender', path: '/einstellungen/freie-tage' },
    { label: 'Kalender abonnieren', icon: 'abo', path: '/einstellungen/kalender-abos' },
    { label: 'Benachrichtigungen', icon: 'glocke', path: '/einstellungen/benachrichtigungen' },
    ...(darfZugaenge
      ? [{ label: 'Zugänge', icon: 'zugang' as const, path: '/einstellungen/zugaenge' }]
      : []),
  ];
}
