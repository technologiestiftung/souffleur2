import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AuthService } from '../../core/auth.service';
import {
  HouseService,
  Kontenrahmen,
  Sphaere,
  Stammdaten,
  StammdatenEingabe,
} from '../../core/house.service';
import { MeldungComponent } from '../../shared/meldung.component';
import { ActionsRegistry } from '../../shell/actions.registry';
import { messagesFrom } from '../calendar/event-dialog.component';
import { einstellungsBereiche } from './bereiche';

/**
 * Die Rechtsformen, die bei Kulturbetrieben vorkommen. Wer eine andere hat,
 * schreibt sie selbst hin -- die Liste ist eine Hilfe, keine Schranke.
 */
export const RECHTSFORMEN = [
  'e. V.',
  'gGmbH',
  'GmbH',
  'gUG (haftungsbeschränkt)',
  'UG (haftungsbeschränkt)',
  'Stiftung',
  'Eigenbetrieb',
  'Regiebetrieb',
  'GbR',
  'Einzelunternehmen',
] as const;

/** Rechtsformen, die die Gemeinnützigkeit schon im Namen tragen. */
const GEMEINNUETZIG = new Set(['gGmbH', 'gUG (haftungsbeschränkt)']);

const ANDERE = 'andere';

/**
 * Die Stammdaten des Hauses: Name, Anschrift, Steuern -- und die beiden
 * Entscheidungen, von denen die Buchhaltung abhängt.
 *
 * Den Kontenrahmen gibt das Steuerbüro vor; souffleur liefert keinen mit.
 * Die Gemeinnützigkeit entscheidet, ob jede Erfolgsbuchung eine Sphäre
 * braucht. Beides steht hier, weil beides das ganze Haus betrifft und nicht
 * eine einzelne Buchung.
 */
@Component({
  selector: 'souffleur-haus',
  imports: [FormsModule, MeldungComponent],
  templateUrl: './haus.component.html',
})
export class HausComponent implements OnInit {
  private readonly house = inject(HouseService);
  private readonly actions = inject(ActionsRegistry);
  private readonly auth = inject(AuthService);

  protected readonly rechtsformen = RECHTSFORMEN;
  protected readonly andere = ANDERE;

  protected readonly kontenrahmen = signal<Kontenrahmen[]>([]);
  protected readonly sphaeren = signal<Sphaere[]>([]);
  protected readonly entwurf = signal<StammdatenEingabe | null>(null);
  protected readonly problems = signal<string[]>([]);
  protected readonly busy = signal(false);
  protected readonly gespeichert = signal(false);

  /**
   * Dass jemand „andere" gewählt hat, auch wenn das Textfeld noch leer ist --
   * sonst spränge die Auswahl auf „—" zurück, ehe man zu schreiben anfängt.
   */
  private readonly andereGewaehlt = signal(false);

  /** Ob die Rechtsform eine aus der Liste ist -- sonst zeigt das Feld „andere" und ein Textfeld. */
  protected readonly rechtsformWahl = computed(() => {
    const form = this.entwurf()?.legalForm;
    if (form && (RECHTSFORMEN as readonly string[]).includes(form)) {
      return form;
    }
    return form || this.andereGewaehlt() ? ANDERE : '';
  });

  protected readonly darfAendern = computed(() => this.auth.can('HouseManage'));

  /** Ob mit dieser Wahl Sphären Pflicht werden -- wie der Server es auch rechnet. */
  protected readonly brauchtSphaeren = computed(() => {
    const entwurf = this.entwurf();
    if (!entwurf) {
      return false;
    }

    const rahmen = this.kontenrahmen().find((k) => k.chart === entwurf.chartOfAccounts);
    return entwurf.isNonProfit || (rahmen?.usesTaxSpheres ?? false);
  });

  async ngOnInit(): Promise<void> {
    this.actions.set('Einstellungen', [], einstellungsBereiche(this.auth.can('AccountsManage')));

    const [stammdaten, kontenrahmen, sphaeren] = await Promise.all([
      this.house.stammdaten(),
      this.house.kontenrahmen(),
      this.house.sphaeren(),
    ]);

    this.kontenrahmen.set(kontenrahmen);
    this.sphaeren.set(sphaeren);
    this.entwurf.set(eingabeAus(stammdaten));
  }

  protected patch(change: Partial<StammdatenEingabe>): void {
    this.gespeichert.set(false);
    this.entwurf.update((current) => (current ? { ...current, ...change } : current));
  }

  /**
   * Aus der Liste gewählt. Eine gGmbH ist gemeinnützig, das sagt schon der
   * Name -- der Schalter folgt, lässt sich aber weiter von Hand stellen.
   */
  protected rechtsformGewaehlt(wahl: string): void {
    this.andereGewaehlt.set(wahl === ANDERE);
    if (wahl === ANDERE) {
      this.patch({ legalForm: '' });
      return;
    }

    this.patch({ legalForm: wahl || null });
    if (GEMEINNUETZIG.has(wahl)) {
      this.patch({ isNonProfit: true });
    }
  }

  protected async speichern(): Promise<void> {
    const entwurf = this.entwurf();
    if (!entwurf) {
      return;
    }

    if (!entwurf.name.trim()) {
      this.problems.set(['Das Haus braucht einen Namen.']);
      return;
    }

    this.busy.set(true);
    this.problems.set([]);
    this.gespeichert.set(false);

    try {
      const gespeichert = await this.house.speichere({
        ...entwurf,
        name: entwurf.name.trim(),
        legalForm: leerZuNull(entwurf.legalForm),
        street: leerZuNull(entwurf.street),
        postalCode: leerZuNull(entwurf.postalCode),
        city: leerZuNull(entwurf.city),
        taxNumber: leerZuNull(entwurf.taxNumber),
        vatId: leerZuNull(entwurf.vatId),
        kskNumber: leerZuNull(entwurf.kskNumber),
      });
      this.entwurf.set(eingabeAus(gespeichert));
      this.gespeichert.set(true);
    } catch (error) {
      this.problems.set(messagesFrom(error));
    } finally {
      this.busy.set(false);
    }
  }
}

function eingabeAus(stammdaten: Stammdaten): StammdatenEingabe {
  const {
    chartLabel: _label,
    requiresTaxSpheres: _spheres,
    hasDemoData: _demo,
    ...eingabe
  } = stammdaten;
  return eingabe;
}

function leerZuNull(wert: string | null): string | null {
  const getrimmt = wert?.trim();
  return getrimmt ? getrimmt : null;
}
