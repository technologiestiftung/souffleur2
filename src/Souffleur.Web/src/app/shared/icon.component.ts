import { Component, input } from '@angular/core';

/**
 * Die Namen aller Zeichen, die es gibt. Als Typ und nicht als Zeichenkette:
 * Ein Tippfehler soll beim Bauen auffallen und nicht als leere Stelle in der
 * Leiste enden.
 */
export type IconName =
  // Module
  | 'uebersicht'
  | 'finanzen'
  | 'personal'
  | 'ticketing'
  | 'aufgaben'
  | 'spielplan'
  | 'kontakte'
  | 'chat'
  | 'kalender'
  | 'dokumente'
  | 'touring'
  | 'produktion'
  | 'abmelden'
  // Bereiche und Aktionen
  | 'einstellungen'
  | 'liste'
  | 'ressource'
  | 'bedarf'
  | 'abo'
  | 'buchung'
  | 'beleg'
  | 'bank'
  | 'etikett'
  | 'funktion'
  | 'zugang'
  | 'gruppe'
  | 'rolle'
  | 'glocke'
  | 'plus'
  | 'download'
  | 'uhr'
  | 'suche'
  | 'griff'
  | 'mail'
  | 'pfeil-auf'
  | 'pfeil-ab'
  | 'mehr'
  | 'zahnrad'
  | 'haus';

/**
 * Ein Zeichen aus einem Guss.
 *
 * Alle im selben Maß gezeichnet: 24er Raster, Strichstärke 2, runde Enden,
 * keine Flächen. So fügen sie sich nebeneinander, statt dass eines fett und
 * das nächste dünn wirkt.
 *
 * Die Pfade stehen hier und nicht in den Vorlagen, die sie benutzen -- sonst
 * steht dasselbe Zeichen an fünf Stellen, und beim sechsten Mal weicht es ab.
 * Eine Zeichenbibliothek wäre eine Abhängigkeit für ein paar Dutzend Pfade.
 */
@Component({
  selector: 'souffleur-icon',
  template: `
    <svg
      [attr.width]="size()"
      [attr.height]="size()"
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      stroke-width="2"
      stroke-linecap="round"
      stroke-linejoin="round"
      aria-hidden="true"
      class="shrink-0"
    >
      @switch (name()) {
        @case ('uebersicht') {
          <rect x="3" y="3" width="7" height="9" rx="1" />
          <rect x="14" y="3" width="7" height="5" rx="1" />
          <rect x="14" y="12" width="7" height="9" rx="1" />
          <rect x="3" y="16" width="7" height="5" rx="1" />
        }
        @case ('finanzen') {
          <rect x="2" y="6" width="20" height="12" rx="2" />
          <circle cx="12" cy="12" r="2.5" />
        }
        @case ('personal') {
          <path d="M15 20v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2" />
          <circle cx="8.5" cy="7" r="3.5" />
          <path d="M22 20v-2a4 4 0 0 0-3-3.87" />
          <path d="M16 3.5a3.5 3.5 0 0 1 0 7" />
        }
        @case ('ticketing') {
          <path
            d="M3 9V7a1 1 0 0 1 1-1h16a1 1 0 0 1 1 1v2a2.5 2.5 0 0 0 0 5v2a1 1 0 0 1-1 1H4a1 1 0 0 1-1-1v-2a2.5 2.5 0 0 0 0-5z"
          />
          <path d="M13 6v2M13 16v2" />
        }
        @case ('touring') {
          <!-- Ein Koffer auf Reisen: Griff oben, Bügel, Mittelnaht. -->
          <rect x="3" y="8" width="18" height="12" rx="2" />
          <path d="M8 8V6a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2" />
          <path d="M3 13h18" />
        }
        @case ('produktion') {
          <!-- Ein Vorhang: zwei Bahnen, in der Mitte geöffnet. -->
          <path d="M3 4h18" />
          <path d="M4 4v16" />
          <path d="M20 4v16" />
          <path d="M4 4c3 3 5 8 5 16" />
          <path d="M20 4c-3 3-5 8-5 16" />
        }
        @case ('aufgaben') {
          <path d="m3 7 2 2 4-4" />
          <path d="m3 17 2 2 4-4" />
          <path d="M13 7h8M13 17h8" />
        }
        @case ('spielplan') {
          <path d="M4 5h16v14H4z" />
          <path d="M4 9h16M9 5v14M15 5v14" />
        }
        @case ('kontakte') {
          <path d="M5 3h13a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2H5z" />
          <path d="M5 8H2M5 12H2M5 16H2" />
          <path d="M12.5 11.5h3.5M12.5 15h2" />
        }
        @case ('chat') {
          <path d="M21 12a8 8 0 0 1-8 8H7l-4 2 1.5-4.5A8 8 0 1 1 21 12z" />
        }
        @case ('kalender') {
          <rect x="3" y="5" width="18" height="16" rx="2" />
          <path d="M8 3v4M16 3v4M3 11h18" />
        }
        @case ('dokumente') {
          <path d="M14 3H7a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2V8z" />
          <path d="M14 3v5h5" />
          <path d="M9 13h6M9 17h4" />
        }
        @case ('einstellungen') {
          <path d="M4 7h10M18 7h2M4 17h2M10 17h10" />
          <circle cx="16" cy="7" r="2" />
          <circle cx="8" cy="17" r="2" />
        }
        @case ('liste') {
          <path d="M9 6h12M9 12h12M9 18h12" />
          <path d="M4 6h.01M4 12h.01M4 18h.01" />
        }
        @case ('ressource') {
          <path d="M12 3 3 7.5v9L12 21l9-4.5v-9z" />
          <path d="m3 7.5 9 4.5 9-4.5M12 12v9" />
        }
        @case ('bedarf') {
          <rect x="5" y="4" width="14" height="17" rx="2" />
          <path d="M9 2h6v4H9z" />
          <path d="M9 11h6M9 15h4" />
        }
        @case ('abo') {
          <path d="M4 11a9 9 0 0 1 9 9M4 4a16 16 0 0 1 16 16" />
          <circle cx="5" cy="19" r="1.5" />
        }
        @case ('buchung') {
          <path d="M4 5a2 2 0 0 1 2-2h12v18H6a2 2 0 0 1-2-2z" />
          <path d="M8 7h7M8 11h7M8 15h4" />
        }
        @case ('beleg') {
          <path d="M5 3h14v18l-2.5-1.5L14 21l-2-1.5L10 21l-2.5-1.5L5 21z" />
          <path d="M9 8h6M9 12h6" />
        }
        @case ('bank') {
          <path d="M3 10h18M5 10v9M19 10v9M3 19h18" />
          <path d="m12 3 9 7H3z" />
        }
        @case ('etikett') {
          <path d="M3 12V5a2 2 0 0 1 2-2h7l9 9-9 9z" />
          <circle cx="7.5" cy="7.5" r="1.5" />
        }
        @case ('funktion') {
          <circle cx="9" cy="8" r="4" />
          <path d="M2 21v-2a4 4 0 0 1 4-4h5" />
          <circle cx="18" cy="17" r="3" />
          <path d="M18 13v1M18 20v1M22 17h-1M15 17h-1" />
        }
        @case ('abmelden') {
          <!-- Eine Tür mit Pfeil hinaus. -->
          <path d="M10 4H6a2 2 0 0 0-2 2v12a2 2 0 0 0 2 2h4" />
          <path d="M14 16l4-4-4-4" />
          <path d="M18 12H9" />
        }
        @case ('zugang') {
          <rect x="4" y="11" width="16" height="10" rx="2" />
          <path d="M8 11V7a4 4 0 0 1 8 0v4" />
        }
        @case ('gruppe') {
          <circle cx="12" cy="8" r="3" />
          <path d="M6 20v-1a6 6 0 0 1 12 0v1" />
          <circle cx="4.5" cy="10" r="2" />
          <circle cx="19.5" cy="10" r="2" />
        }
        @case ('rolle') {
          <path d="M12 3 4 6v6c0 4.5 3.4 8 8 9 4.6-1 8-4.5 8-9V6z" />
          <path d="m9 12 2 2 4-4" />
        }
        @case ('suche') {
          <circle cx="11" cy="11" r="7" />
          <path d="M20 20l-3.5-3.5" />
        }
        @case ('griff') {
          <!-- Sechs Punkte: der Griff zum Ziehen, wie man ihn kennt. -->
          <path d="M9 6h.01M15 6h.01M9 12h.01M15 12h.01M9 18h.01M15 18h.01" stroke-width="3" />
        }
        @case ('mail') {
          <path d="M4 5h16a1 1 0 0 1 1 1v12a1 1 0 0 1-1 1H4a1 1 0 0 1-1-1V6a1 1 0 0 1 1-1z" />
          <path d="m3 7 9 6 9-6" />
        }
        @case ('glocke') {
          <path d="M6 8a6 6 0 0 1 12 0c0 7 3 9 3 9H3s3-2 3-9" />
          <path d="M10.3 21a1.94 1.94 0 0 0 3.4 0" />
        }
        @case ('plus') {
          <path d="M12 5v14M5 12h14" />
        }
        @case ('download') {
          <path d="M12 3v12M7 10l5 5 5-5M5 21h14" />
        }
        @case ('uhr') {
          <circle cx="12" cy="12" r="9" />
          <path d="M12 7v5l3 2" />
        }
        @case ('haus') {
          <path d="m3 11 9-8 9 8" />
          <path d="M5 10v10h5v-6h4v6h5V10" />
        }
        @case ('pfeil-auf') {
          <path d="m6 15 6-6 6 6" />
        }
        @case ('pfeil-ab') {
          <path d="m6 9 6 6 6-6" />
        }
        @case ('mehr') {
          <!-- Drei Punkte: dahinter steckt mehr. -->
          <path d="M5 12h.01M12 12h.01M19 12h.01" stroke-width="3" />
        }
        @case ('zahnrad') {
          <circle cx="12" cy="12" r="3" />
          <path
            d="M19.4 15a1.7 1.7 0 0 0 .3 1.8l.1.1a2 2 0 1 1-2.8 2.8l-.1-.1a1.7 1.7 0 0 0-1.8-.3 1.7 1.7 0 0 0-1 1.5V21a2 2 0 1 1-4 0v-.1a1.7 1.7 0 0 0-1.1-1.5 1.7 1.7 0 0 0-1.8.3l-.1.1a2 2 0 1 1-2.8-2.8l.1-.1a1.7 1.7 0 0 0 .3-1.8 1.7 1.7 0 0 0-1.5-1H3a2 2 0 1 1 0-4h.1a1.7 1.7 0 0 0 1.5-1.1 1.7 1.7 0 0 0-.3-1.8l-.1-.1a2 2 0 1 1 2.8-2.8l.1.1a1.7 1.7 0 0 0 1.8.3H9a1.7 1.7 0 0 0 1-1.5V3a2 2 0 1 1 4 0v.1a1.7 1.7 0 0 0 1 1.5 1.7 1.7 0 0 0 1.8-.3l.1-.1a2 2 0 1 1 2.8 2.8l-.1.1a1.7 1.7 0 0 0-.3 1.8V9a1.7 1.7 0 0 0 1.5 1H21a2 2 0 1 1 0 4h-.1a1.7 1.7 0 0 0-1.5 1z"
          />
        }
      }
    </svg>
  `,
})
export class IconComponent {
  readonly name = input.required<IconName>();

  /** In Pixeln. 15 in den Leisten, 16 an Knöpfen. */
  readonly size = input(16);
}
