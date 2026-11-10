import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

/** Eine Woche Herzschlag -- dieselben Felder wie am Server. */
export interface AktivitaetsWoche {
  week: string;
  tasks: number;
  events: number;
  vouchers: number;
  documents: number;
  total: number;
}

interface Saeule {
  x: number;
  teile: { y: number; h: number; farbe: string }[];
  titel: string;
}

const ARTEN: { key: keyof AktivitaetsWoche; label: string; farbe: string }[] = [
  { key: 'tasks', label: 'Aufgaben', farbe: 'var(--color-chart-1)' },
  { key: 'events', label: 'Termine', farbe: 'var(--color-chart-2)' },
  { key: 'vouchers', label: 'Belege', farbe: 'var(--color-chart-3)' },
  { key: 'documents', label: 'Dokumente', farbe: 'var(--color-chart-4)' },
];

/**
 * Der Herzschlag einer Produktion: was Woche für Woche an ihr passiert ist.
 *
 * Klein ist es eine Linie wie ein EKG -- genug, um in der Liste zu sehen, ob
 * eine Produktion lebt oder liegt. Groß werden es Säulen je Woche, nach Art
 * gefärbt, mit Monatsmarken darunter: dieselben Zahlen, mehr Auflösung.
 */
@Component({
  selector: 'souffleur-aktivitaet',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'inline-block align-middle' },
  template: `
    @if (groesse() === 'klein') {
      <svg
        [attr.width]="breite()"
        height="16"
        [attr.viewBox]="'0 0 ' + breite() + ' 16'"
        role="img"
        [attr.aria-label]="beschreibung()"
        class="block overflow-visible"
      >
        <polyline
          [attr.points]="linie()"
          fill="none"
          [attr.stroke]="summe() > 0 ? 'var(--color-accent)' : 'var(--color-border-strong)'"
          stroke-width="1.5"
          stroke-linejoin="round"
          stroke-linecap="round"
        />
      </svg>
    } @else {
      <figure class="m-0" role="group" [attr.aria-label]="beschreibung()">
        <svg
          [attr.viewBox]="'0 0 ' + breite() + ' 48'"
          class="block h-12 w-full"
          preserveAspectRatio="none"
          aria-hidden="true"
        >
          @for (saeule of saeulen(); track saeule.x) {
            <g>
              <title>{{ saeule.titel }}</title>
              <rect
                [attr.x]="saeule.x"
                y="0"
                [attr.width]="schritt() - 2"
                height="40"
                fill="transparent"
              />
              @for (teil of saeule.teile; track $index) {
                <rect
                  [attr.x]="saeule.x"
                  [attr.y]="teil.y"
                  [attr.width]="schritt() - 2"
                  [attr.height]="teil.h"
                  [attr.fill]="teil.farbe"
                  rx="1"
                />
              }
              @if (saeule.teile.length === 0) {
                <rect
                  [attr.x]="saeule.x"
                  y="39"
                  [attr.width]="schritt() - 2"
                  height="1"
                  fill="var(--color-border)"
                />
              }
            </g>
          }
          @for (marke of marken(); track marke.x) {
            <text
              [attr.x]="marke.x"
              y="47"
              font-size="7"
              fill="var(--color-content-faint)"
              font-family="inherit"
            >
              {{ marke.label }}
            </text>
          }
        </svg>
        <figcaption class="mt-1 flex flex-wrap gap-x-3 gap-y-0.5 text-[10px] text-content-muted">
          @for (art of arten; track art.key) {
            <span class="inline-flex items-center gap-1">
              <span
                class="inline-block size-2 rounded-sm"
                [style.background]="art.farbe"
                aria-hidden="true"
              ></span>
              {{ art.label }}
            </span>
          }
          <span class="ml-auto tabular">{{ summe() }} in {{ wochen().length }} Wochen</span>
        </figcaption>
      </figure>
    }
  `,
})
export class AktivitaetComponent {
  readonly wochen = input.required<readonly AktivitaetsWoche[]>();
  readonly groesse = input<'klein' | 'gross'>('klein');

  /** Klein reicht ein Vierteljahr, groß das ganze halbe. */
  readonly anzahl = input<number | null>(null);

  protected readonly arten = ARTEN;

  protected readonly sichtbar = computed(() => {
    const alle = this.wochen();
    const n = this.anzahl() ?? (this.groesse() === 'klein' ? 12 : alle.length);
    return alle.slice(Math.max(0, alle.length - n));
  });

  protected readonly schritt = computed(() => (this.groesse() === 'klein' ? 6 : 10));
  protected readonly breite = computed(() => this.sichtbar().length * this.schritt());
  protected readonly summe = computed(() => this.sichtbar().reduce((s, w) => s + w.total, 0));
  protected readonly maximum = computed(() => Math.max(1, ...this.sichtbar().map((w) => w.total)));

  /** Die EKG-Linie: eine Stütze je Woche, Höhe nach Gesamtzahl. */
  protected readonly linie = computed(() => {
    const wochen = this.sichtbar();
    const max = this.maximum();
    const schritt = this.schritt();
    return wochen
      .map((w, i) => {
        const x = i * schritt + schritt / 2;
        const y = 14 - (w.total / max) * 12;
        return `${x.toFixed(1)},${y.toFixed(1)}`;
      })
      .join(' ');
  });

  protected readonly saeulen = computed<Saeule[]>(() => {
    const max = this.maximum();
    const schritt = this.schritt();
    return this.sichtbar().map((w, i) => {
      let y = 40;
      const teile: Saeule['teile'] = [];
      for (const art of ARTEN) {
        const wert = w[art.key] as number;
        if (wert > 0) {
          const h = (wert / max) * 38;
          y -= h;
          teile.push({ y, h, farbe: art.farbe });
        }
      }
      return { x: i * schritt + 1, teile, titel: titel(w) };
    });
  });

  /** Eine Monatsmarke dort, wo ein Monat beginnt -- nicht an jeder Woche. */
  protected readonly marken = computed(() => {
    const schritt = this.schritt();
    const marken: { x: number; label: string }[] = [];
    let letzter = -1;
    this.sichtbar().forEach((w, i) => {
      const tag = new Date(`${w.week}T12:00:00Z`);
      const monat = tag.getUTCMonth();
      // Am rechten Rand hat kein Wort mehr Platz.
      const passt = i * schritt + 1 <= this.breite() - 14;
      if (monat !== letzter && (i === 0 || tag.getUTCDate() <= 7) && passt) {
        marken.push({
          x: i * schritt + 1,
          label: tag.toLocaleDateString('de-DE', { month: 'short' }).replace('.', ''),
        });
        letzter = monat;
      }
    });
    return marken;
  });

  protected readonly beschreibung = computed(() => {
    const wochen = this.sichtbar();
    const summe = this.summe();
    if (summe === 0) {
      return `Keine Aktivität in den letzten ${wochen.length} Wochen`;
    }
    const letzte = wochen.slice(-4).reduce((s, w) => s + w.total, 0);
    return `Aktivität: ${summe} Ereignisse in ${wochen.length} Wochen, davon ${letzte} im letzten Monat`;
  });
}

function titel(w: AktivitaetsWoche): string {
  const tag = new Date(`${w.week}T12:00:00Z`);
  const von = tag.toLocaleDateString('de-DE', { day: 'numeric', month: 'short' });
  if (w.total === 0) {
    return `Woche ab ${von}: nichts`;
  }
  const teile = ARTEN.filter((a) => (w[a.key] as number) > 0).map((a) => `${w[a.key]} ${a.label}`);
  return `Woche ab ${von}: ${teile.join(', ')}`;
}
