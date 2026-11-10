import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { Contact, ContactsService } from './contacts.service';

export interface MailRecipient {
  address: string;
  name: string | null;
  contactId: string | null;
}

export interface Mail {
  id: string;
  direction: 'incoming' | 'outgoing';
  fromAddress: string;
  fromName: string | null;
  contactId: string | null;
  senderName: string | null;
  recipients: MailRecipient[];
  subject: string;
  body: string;
  sentAt: string;
  readAt: string | null;
  isUnread: boolean;
}

export interface MailStatus {
  canSend: boolean;
  sendMode: 'Off' | 'Smtp' | 'Log';
  canReceive: boolean;
  from: string | null;
}

/** Ein gewählter Empfänger beim Schreiben: ein Kontakt oder eine Adresse von Hand. */
export interface Empfaenger {
  contactId: string | null;
  address: string;
  label: string;
}

export interface Entwurf {
  an: Empfaenger[];
  betreff: string;
  text: string;
}

export type MailAnsicht = { art: 'liste' } | { art: 'lesen'; id: string } | { art: 'schreiben' };

/**
 * Die Post des Hauses in der Kommunikationsleiste. Hält neben den Mails auch
 * fest, was die Leiste gerade zeigt und was im Entwurf steht -- die Leiste
 * baut ihren Inhalt um, wenn der Chat aufgeht, und der halb geschriebene
 * Text soll das überleben.
 */
@Injectable({ providedIn: 'root' })
export class MailService {
  private readonly http = inject(HttpClient);
  private readonly contacts = inject(ContactsService);

  private readonly mailList = signal<Mail[]>([]);
  private readonly statusInfo = signal<MailStatus | null>(null);
  private readonly unreadCount = signal(0);

  readonly mails = this.mailList.asReadonly();
  readonly status = this.statusInfo.asReadonly();
  readonly unread = this.unreadCount.asReadonly();

  readonly ansicht = signal<MailAnsicht>({ art: 'liste' });
  readonly entwurf = signal<Entwurf>({ an: [], betreff: '', text: '' });

  /**
   * Die Kontakte für das Feld „An". Hier und nicht im Feld selbst: Die Leiste
   * baut das Feld neu, sobald sie vom Nebeneinander in die ganze Breite
   * wechselt -- und was ein weggeworfenes Feld geladen hatte, wäre weg.
   */
  readonly kontakte = signal<Contact[]>([]);

  /** Lesen oder Schreiben braucht die ganze Leiste; der Chat tritt dann zurück. */
  readonly beschaeftigt = computed(() => this.ansicht().art !== 'liste');

  readonly offen = computed(() => {
    const ansicht = this.ansicht();
    return ansicht.art === 'lesen'
      ? (this.mailList().find((m) => m.id === ansicht.id) ?? null)
      : null;
  });

  async load(): Promise<void> {
    try {
      const [status, liste] = await Promise.all([
        this.statusInfo() ?? firstValueFrom(this.http.get<MailStatus>('/api/mail/status')),
        firstValueFrom(this.http.get<{ unread: number; mails: Mail[] }>('/api/mail')),
      ]);
      this.statusInfo.set(status);
      this.mailList.set(liste.mails);
      this.unreadCount.set(liste.unread);
    } catch {
      // Nicht angemeldet: dann gibt es nichts zu zeigen.
      this.mailList.set([]);
      this.unreadCount.set(0);
    }
  }

  async lesen(mail: Mail): Promise<void> {
    this.ansicht.set({ art: 'lesen', id: mail.id });
    if (mail.isUnread) {
      const gelesen = await firstValueFrom(this.http.post<Mail>(`/api/mail/${mail.id}/read`, {}));
      this.mailList.update((liste) => liste.map((m) => (m.id === gelesen.id ? gelesen : m)));
      this.unreadCount.update((n) => Math.max(0, n - 1));
    }
  }

  schreiben(vorlage?: Partial<Entwurf>): void {
    if (vorlage) {
      this.entwurf.update((e) => ({ ...e, ...vorlage }));
    }
    this.ansicht.set({ art: 'schreiben' });
    if (this.kontakte().length === 0) {
      void this.contacts.list().then((liste) => this.kontakte.set(liste));
    }
  }

  /** Antworten heißt: an den Absender, mit dem Betreff davor. */
  antworten(mail: Mail): void {
    const betreff = /^(re|aw):/i.test(mail.subject) ? mail.subject : `Re: ${mail.subject}`;
    this.schreiben({
      an: [
        {
          contactId: mail.contactId,
          address: mail.fromAddress,
          label: mail.fromName ?? mail.fromAddress,
        },
      ],
      betreff,
      text: '',
    });
  }

  zurueck(): void {
    this.ansicht.set({ art: 'liste' });
  }

  async senden(): Promise<void> {
    const { an, betreff, text } = this.entwurf();
    const gesendet = await firstValueFrom(
      this.http.post<Mail>('/api/mail', {
        recipients: an.map((e) =>
          e.contactId ? { contactId: e.contactId } : { address: e.address },
        ),
        subject: betreff,
        body: text,
      }),
    );
    this.mailList.update((liste) => [gesendet, ...liste]);
    this.entwurf.set({ an: [], betreff: '', text: '' });
    this.ansicht.set({ art: 'liste' });
  }

  /** Wen eine Mail betrifft: den Absender, oder bei eigenen die Empfänger. */
  gegenueber(mail: Mail): string {
    if (mail.direction === 'incoming') {
      return mail.fromName ?? mail.fromAddress;
    }
    return mail.recipients.map((r) => r.name ?? r.address).join(', ');
  }
}
