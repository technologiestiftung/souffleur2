import { DatePipe } from '@angular/common';
import { Component, ElementRef, computed, effect, inject, signal, viewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Contact } from '../core/contacts.service';
import { Empfaenger, Mail, MailService } from '../core/mail.service';
import { messagesFrom } from '../features/calendar/event-dialog.component';
import { IconComponent } from '../shared/icon.component';

/**
 * E-Mail in der Kommunikationsleiste: die Post des Hauses lesen und kurz
 * antworten, oder eine neue an Kontakte schreiben.
 *
 * Drei Ansichten, die sich die Leiste teilen: die Liste unter dem Chat,
 * eine Mail aufgeschlagen, der Entwurf. Welche gilt, weiß der Service --
 * die Leiste richtet sich danach, ob sie dem Chat noch Platz lässt.
 */
@Component({
  selector: 'souffleur-mail-panel',
  imports: [FormsModule, DatePipe, IconComponent],
  templateUrl: './mail-panel.component.html',
  host: { class: 'flex min-h-0 flex-1 flex-col' },
})
export class MailPanelComponent {
  protected readonly mail = inject(MailService);

  private readonly anFeld = viewChild<ElementRef<HTMLInputElement>>('anFeld');

  protected readonly suche = signal('');
  protected readonly sending = signal(false);
  protected readonly problems = signal<string[]>([]);

  constructor() {
    // Das Feld „An" bekommt den Cursor, sobald der Entwurf aufgeht -- auch
    // dann, wenn die Leiste dieses Feld gerade erst neu aufgebaut hat.
    effect(() => {
      if (this.mail.ansicht().art === 'schreiben') {
        setTimeout(() => this.anFeld()?.nativeElement.focus());
      }
    });
  }

  /** Kontakte mit Adresse, die zum Getippten passen und noch nicht gewählt sind. */
  protected readonly vorschlaege = computed(() => {
    const text = this.suche().trim().toLowerCase();
    if (!text) {
      return [];
    }
    const gewaehlt = new Set(this.mail.entwurf().an.map((e) => e.contactId ?? e.address));
    return this.mail
      .kontakte()
      .filter((c) => c.email && c.isActive && !gewaehlt.has(c.id))
      .filter((c) => c.name.toLowerCase().includes(text) || c.email!.toLowerCase().includes(text))
      .slice(0, 8);
  });

  protected readonly sendbar = computed(() => {
    const e = this.mail.entwurf();
    return e.an.length > 0 && e.betreff.trim().length > 0 && e.text.trim().length > 0;
  });

  protected schreiben(): void {
    this.problems.set([]);
    this.mail.schreiben();
  }

  protected antworten(mail: Mail): void {
    this.problems.set([]);
    this.mail.antworten(mail);
  }

  protected zurueck(): void {
    this.mail.zurueck();
  }

  protected waehlen(kontakt: Contact): void {
    this.hinzu({ contactId: kontakt.id, address: kontakt.email!, label: kontakt.name });
  }

  /**
   * Enter im Feld „An": der eine passende Kontakt, sonst die getippte
   * Adresse, wenn sie wie eine aussieht. So kommt auch jemand in die Mail,
   * der noch nicht im Adressbuch steht.
   */
  protected eingabe(event: Event): void {
    event.preventDefault();
    const [einziger] = this.vorschlaege();
    const text = this.suche().trim();
    if (this.vorschlaege().length === 1) {
      this.waehlen(einziger);
    } else if (/^[^\s@]+@[^\s@]+$/.test(text)) {
      this.hinzu({ contactId: null, address: text, label: text });
    }
  }

  /** Rückschritt im leeren Feld nimmt den letzten Empfänger wieder raus. */
  protected rueckschritt(): void {
    if (!this.suche()) {
      this.mail.entwurf.update((e) => ({ ...e, an: e.an.slice(0, -1) }));
    }
  }

  protected entfernen(empfaenger: Empfaenger): void {
    this.mail.entwurf.update((e) => ({ ...e, an: e.an.filter((x) => x !== empfaenger) }));
    this.anFeld()?.nativeElement.focus();
  }

  protected betreff(wert: string): void {
    this.mail.entwurf.update((e) => ({ ...e, betreff: wert }));
  }

  protected text(wert: string): void {
    this.mail.entwurf.update((e) => ({ ...e, text: wert }));
  }

  protected async senden(): Promise<void> {
    if (!this.sendbar() || this.sending()) {
      return;
    }
    this.sending.set(true);
    this.problems.set([]);
    try {
      await this.mail.senden();
    } catch (error) {
      this.problems.set(messagesFrom(error));
    } finally {
      this.sending.set(false);
    }
  }

  private hinzu(empfaenger: Empfaenger): void {
    this.mail.entwurf.update((e) => ({ ...e, an: [...e.an, empfaenger] }));
    this.suche.set('');
    this.anFeld()?.nativeElement.focus();
  }
}
