import { DatePipe } from '@angular/common';
import {
  Component,
  DestroyRef,
  ElementRef,
  computed,
  effect,
  inject,
  signal,
  untracked,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router } from '@angular/router';
import { filter } from 'rxjs/operators';
import { FormsModule } from '@angular/forms';
import { AuthService } from '../core/auth.service';
import {
  ChatMessage,
  ChatService,
  Conversation,
  ConversationKind,
  Mentionable,
  Participant,
} from '../core/chat.service';
import { LayoutService } from '../core/layout.service';
import { MailService } from '../core/mail.service';
import { ModulesService } from '../core/modules.service';
import { DialogComponent } from '../shared/dialog.component';
import { MailPanelComponent } from './mail-panel.component';

/** Ein Stück Nachrichtentext: entweder Wort für Wort, oder eine hervorgehobene Erwähnung. */
interface Textteil {
  text: string;
  erwaehnung: boolean;
}

/**
 * Rechts wird geredet: oben der Chat, darunter die Post des Hauses. Eine
 * geöffnete Unterhaltung oder eine aufgeschlagene Mail nimmt die ganze
 * Leiste; sonst teilen sich beide den Platz.
 */
@Component({
  selector: 'souffleur-chat-sidebar',
  imports: [FormsModule, DatePipe, MailPanelComponent, DialogComponent],
  templateUrl: './chat-sidebar.component.html',
  host: {
    class: 'relative flex shrink-0',
    '(document:click)': 'aussen($event)',
    '(document:keydown.escape)': 'escape()',
    '(mouseenter)': 'schweben(true)',
    '(mouseleave)': 'schweben(false)',
  },
})
export class ChatSidebarComponent {
  protected readonly layout = inject(LayoutService);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  protected readonly chat = inject(ChatService);
  protected readonly mail = inject(MailService);
  protected readonly modules = inject(ModulesService);

  /** Die Zahl am zugeklappten Streifen: Chat und Post zusammen. */
  protected readonly ungelesen = computed(() => this.chat.unreadTotal() + this.mail.unread());

  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  constructor() {
    // `?chat=<id>` in der Adresse öffnet die Unterhaltung -- so verlinken
    // Glocke und Suche. Die Leiste steht außerhalb des Router-Outlets und
    // bekommt die Parameter deshalb nicht gereicht; sie hört auf den Router.
    this.router.events
      .pipe(
        filter((e) => e instanceof NavigationEnd),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe(() => {
        const id = this.router.routerState.snapshot.root.queryParamMap.get('chat');
        if (id && this.modules.isEnabled('Chat')) {
          void this.oeffneAusAdresse(id);
        }
      });

    // Laden, sobald die Leiste offen ist -- nicht erst beim Klick aufs
    // Aufklappen. Ihr Zustand wird gemerkt, sie kann also beim Start schon
    // offen sein, und dann hätte sie sonst niemand gefüllt. Die Verbindung
    // steht ab hier auch ohne offene Unterhaltung: Eine neue Direktnachricht
    // soll in der Liste erscheinen, nicht erst nach einem Neuladen.
    effect(() => {
      if (this.layout.chatOpen()) {
        untracked(() => {
          if (this.modules.isEnabled('Chat')) {
            void this.chat.load();
            void this.chat.loadPeople();
            void this.chat.ensureConnected();
          }
          void this.mail.load();
        });
      }
    });
  }

  protected readonly draft = signal('');
  protected readonly newChannel = signal('');
  protected readonly newChannelPrivate = signal(false);
  /** Ein Kanal für eine Gruppe: ihre Mitglieder sind drin, auch die späteren. */
  protected readonly newChannelGroup = signal('');

  /** Worauf die nächste Nachricht antwortet. */
  protected readonly antwortAuf = signal<ChatMessage | null>(null);

  /** Die eigene Nachricht, die gerade geändert wird, und ihr neuer Text. */
  protected readonly aendert = signal<string | null>(null);
  protected readonly aenderText = signal('');
  protected readonly showNewChannel = signal(false);
  protected readonly showArchived = signal(false);
  protected readonly sending = signal(false);
  protected readonly menuOpen = signal(false);

  /** Welches Fenster über der Leiste liegt: Teilnehmer, neue Direktnachricht, Rückfrage vor dem Löschen. */
  protected readonly fenster = signal<'keins' | 'teilnehmer' | 'direkt' | 'loeschen'>('keins');
  protected readonly teilnehmer = signal<Participant[]>([]);
  protected readonly suche = signal('');
  protected readonly fehler = signal('');

  // ── Erwähnungen ──────────────────────────────────────────────────────
  /** Was hinter dem @ steht, solange die Vorschlagsliste offen ist; null heißt zu. */
  protected readonly mentionQuery = signal<string | null>(null);
  protected readonly mentionIndex = signal(0);
  /** Was per Vorschlagsliste eingefügt wurde -- beim Senden zählt nur, was noch im Text steht. */
  private readonly eingefuegt = signal<Mentionable[]>([]);

  protected readonly vorschlaege = computed<Mentionable[]>(() => {
    const query = this.mentionQuery();
    if (query === null) {
      return [];
    }
    const needle = query.toLocaleLowerCase('de');
    return this.chat
      .mentionables()
      .filter((m) => m.name.toLocaleLowerCase('de').includes(needle))
      .slice(0, 6);
  });

  /** Die Nachrichten mit zerlegtem Text, damit Erwähnungen hervortreten. */
  protected readonly nachrichten = computed(() => {
    const namen = this.chat
      .mentionables()
      .map((m) => m.name)
      .concat(this.auth.user()?.displayName ?? [])
      .sort((a, b) => b.length - a.length);
    const nachId = new Map(this.chat.messages().map((m) => [m.id, m]));
    return this.chat.messages().map((m) => ({
      ...m,
      teile: zerlegen(m.text, namen),
      // Worauf geantwortet wird -- steht es nicht mehr in den geladenen, bleibt
      // nur der Hinweis, dass es eine Antwort ist.
      zitat: m.inReplyToId ? zitatVon(nachId.get(m.inReplyToId)) : null,
    }));
  });

  protected get enabled(): boolean {
    return this.modules.isEnabled('Chat');
  }

  protected get me(): string | undefined {
    return this.auth.user()?.id;
  }

  protected toggle(): void {
    this.layout.toggleChat();
  }

  protected escape(): void {
    if (this.mentionQuery() !== null) {
      this.mentionQuery.set(null);
      return;
    }
    if (this.menuOpen()) {
      this.menuOpen.set(false);
      return;
    }
    if (this.fenster() !== 'keins') {
      // Das Fenster schließt sich selbst über seinen Escape-Griff.
      return;
    }
    this.layout.closeChatOverlay();
  }

  private async oeffneAusAdresse(id: string): Promise<void> {
    if (!this.layout.chatOpen()) {
      this.layout.toggleChat();
    }
    await this.chat.load();
    await this.chat.openConversation(id);
  }

  /**
   * Zugeklappt öffnet sich die Leiste schon, wenn die Maus auf den Streifen
   * kommt, und schließt, wenn sie wieder geht -- mit kleiner Verzögerung,
   * damit ein Vorbeifahren nichts auslöst. Solange etwas in der Leiste
   * den Fokus hat, bleibt sie trotz Maus draußen offen: Wer gerade tippt,
   * will nicht, dass ihm das Feld unter den Fingern verschwindet.
   */
  protected schweben(drin: boolean): void {
    clearTimeout(this.schwebt);
    if (this.layout.chatDocked()) {
      return;
    }
    this.schwebt = setTimeout(
      () => {
        if (drin) {
          this.layout.openChatOverlay();
        } else if (
          !this.host.nativeElement.contains(document.activeElement) &&
          this.fenster() === 'keins'
        ) {
          this.layout.closeChatOverlay();
        }
      },
      drin ? 120 : 300,
    );
  }

  private schwebt?: ReturnType<typeof setTimeout>;

  /** Gelöst ist die Leiste ein Menü: Ein Klick daneben schließt sie. */
  protected aussen(event: MouseEvent): void {
    const target = event.target as Node;
    if (!this.host.nativeElement.contains(target)) {
      this.menuOpen.set(false);
      // Ein Fenster liegt außerhalb der Leiste im Baum; ein Klick darin ist kein Klick daneben.
      if (this.layout.chatOpen() && this.fenster() === 'keins') {
        this.layout.closeChatOverlay();
      }
    }
  }

  protected async open(conversation: Conversation): Promise<void> {
    this.menuOpen.set(false);
    this.fehler.set('');
    await this.chat.openConversation(conversation.id);
  }

  protected titleOf(conversation: Conversation): string {
    return this.chat.title(conversation);
  }

  /** Die Zeile unter dem Titel: geschlossen, am Gegenstand, wie viele dabei sind. */
  protected untertitel(conversation: Conversation): string {
    if (conversation.subjectLabel) {
      return 'am Gegenstand';
    }
    if (conversation.kind === ConversationKind.Direct) {
      return 'Direktnachricht';
    }
    const n = conversation.participants.length;
    const wer = n === 1 ? '1 Person' : `${n} Personen`;
    return conversation.isPrivate ? `geschlossen · ${wer}` : wer;
  }

  protected isMine(authorId: string): boolean {
    return this.auth.user()?.id === authorId;
  }

  protected isChannel(conversation: Conversation): boolean {
    return conversation.kind === ConversationKind.Channel;
  }

  // ── Schreiben ────────────────────────────────────────────────────────

  /** Beim Tippen: Steht vor dem Cursor ein angefangenes @, geht die Vorschlagsliste auf. */
  protected tippen(textarea: HTMLTextAreaElement): void {
    this.draft.set(textarea.value);
    const davor = textarea.value.slice(0, textarea.selectionStart ?? textarea.value.length);
    const treffer = /(?:^|\s)@([^\s@]*)$/.exec(davor);
    this.mentionQuery.set(treffer ? treffer[1] : null);
    this.mentionIndex.set(0);
  }

  /**
   * Tasten im Nachrichtenfeld: Bei offener Vorschlagsliste wählt Enter den
   * Namen statt zu senden, wie in Slack; Pfeile wandern, Tab übernimmt auch.
   */
  protected taste(event: KeyboardEvent, textarea: HTMLTextAreaElement): void {
    const offen = this.mentionQuery() !== null && this.vorschlaege().length > 0;

    if (offen && (event.key === 'ArrowDown' || event.key === 'ArrowUp')) {
      event.preventDefault();
      const n = this.vorschlaege().length;
      const schritt = event.key === 'ArrowDown' ? 1 : -1;
      this.mentionIndex.set((this.mentionIndex() + schritt + n) % n);
      return;
    }

    if (offen && (event.key === 'Enter' || event.key === 'Tab')) {
      event.preventDefault();
      this.waehle(this.vorschlaege()[this.mentionIndex()], textarea);
      return;
    }

    if (event.key === 'Enter' && !event.shiftKey) {
      event.preventDefault();
      void this.send();
    }
  }

  /** Setzt „@Name " an die Stelle des angefangenen @ und merkt sich, wer gemeint ist. */
  protected waehle(wen: Mentionable | undefined, textarea: HTMLTextAreaElement): void {
    if (!wen) {
      return;
    }
    const cursor = textarea.selectionStart ?? textarea.value.length;
    const davor = textarea.value.slice(0, cursor);
    const danach = textarea.value.slice(cursor);
    const start = davor.lastIndexOf('@');
    const neu = `${davor.slice(0, start)}@${wen.name} ${danach}`;

    this.draft.set(neu);
    this.eingefuegt.update((liste) =>
      liste.some((m) => m.id === wen.id) ? liste : [...liste, wen],
    );
    this.mentionQuery.set(null);

    textarea.value = neu;
    const stelle = start + wen.name.length + 2;
    textarea.setSelectionRange(stelle, stelle);
    textarea.focus();
  }

  protected async send(): Promise<void> {
    const text = this.draft().trim();
    if (!text || this.sending()) {
      return;
    }

    // Nur, wer noch im Text steht: Wer den Namen wieder gelöscht hat, meint ihn nicht mehr.
    const erwaehnt = this.eingefuegt().filter((m) => text.includes(`@${m.name}`));

    this.sending.set(true);
    this.fehler.set('');
    try {
      await this.chat.post(text, erwaehnt, this.antwortAuf()?.id ?? null);
      this.draft.set('');
      this.eingefuegt.set([]);
      this.antwortAuf.set(null);
    } catch (e) {
      this.fehler.set(problem(e, 'Die Nachricht wurde nicht gesendet.'));
    } finally {
      this.sending.set(false);
    }
  }

  // ── Antworten und Ändern ─────────────────────────────────────────────

  protected antworten(message: ChatMessage): void {
    this.antwortAuf.set(message);
    document.getElementById('chat-draft')?.focus();
  }

  /** Zur Nachricht springen, auf die geantwortet wurde. */
  protected zeigeOriginal(id: string): void {
    const ziel = document.getElementById(`nachricht-${id}`);
    ziel?.scrollIntoView({ block: 'center', behavior: 'smooth' });
    ziel?.classList.add('ring-2', 'ring-brand');
    setTimeout(() => ziel?.classList.remove('ring-2', 'ring-brand'), 1500);
  }

  protected beginneAendern(message: ChatMessage): void {
    this.aendert.set(message.id);
    this.aenderText.set(message.text);
  }

  protected async aenderungSpeichern(): Promise<void> {
    const id = this.aendert();
    const text = this.aenderText().trim();
    if (!id || !text) {
      return;
    }
    this.fehler.set('');
    try {
      await this.chat.edit(id, text);
      this.aendert.set(null);
    } catch (e) {
      this.fehler.set(problem(e, 'Die Änderung wurde nicht gespeichert.'));
    }
  }

  // ── Kanäle und Direktnachrichten ─────────────────────────────────────

  protected async createChannel(): Promise<void> {
    const name = this.newChannel().trim();
    if (!name) {
      return;
    }

    const gruppe = this.newChannelGroup() || null;
    const created = await this.chat.createChannel(name, this.newChannelPrivate(), gruppe);
    this.newChannel.set('');
    this.newChannelPrivate.set(false);
    this.newChannelGroup.set('');
    this.showNewChannel.set(false);

    // Ein Kanal für eine Gruppe hat seine Leute schon. Wer ihn anlegt, ohne
    // selbst in der Gruppe zu sein, sieht einen geschlossenen danach nicht.
    if (gruppe) {
      if (this.chat.conversations().some((c) => c.id === created.id)) {
        await this.chat.openConversation(created.id);
      }
      return;
    }

    // Gleich die Frage, wer dabei sein soll -- ein Kanal allein ist keiner.
    await this.chat.openConversation(created.id);
    await this.zeigeTeilnehmer();
  }

  protected gruppeName(id: string | null): string {
    return this.chat.groups().find((g) => g.id === id)?.name ?? 'eine Gruppe';
  }

  protected async direktAn(userId: string): Promise<void> {
    this.fenster.set('keins');
    this.suche.set('');
    const conversation = await this.chat.direct(userId);
    await this.chat.openConversation(conversation.id);
  }

  /** Die Personen zum Auswählen -- durch das Suchfeld gefiltert, ohne mich und ohne die, die schon dabei sind. */
  protected readonly auswahlPersonen = computed(() => {
    const needle = this.suche().trim().toLocaleLowerCase('de');
    const drin = new Set(this.teilnehmer().map((t) => t.userId));
    return this.chat
      .people()
      .filter((p) => p.id !== this.me)
      .filter((p) => this.fenster() === 'direkt' || !drin.has(p.id))
      .filter((p) => !needle || p.name.toLocaleLowerCase('de').includes(needle));
  });

  protected readonly auswahlGruppen = computed(() => {
    const needle = this.suche().trim().toLocaleLowerCase('de');
    return this.chat
      .groups()
      .filter((g) => !needle || g.name.toLocaleLowerCase('de').includes(needle));
  });

  // ── Teilnehmer ───────────────────────────────────────────────────────

  protected async zeigeTeilnehmer(): Promise<void> {
    const offen = this.chat.open();
    if (!offen) {
      return;
    }
    this.menuOpen.set(false);
    this.suche.set('');
    this.teilnehmer.set(await this.chat.participants(offen.id));
    this.fenster.set('teilnehmer');
  }

  protected async aufnehmen(userIds: string[], groupIds: string[]): Promise<void> {
    const offen = this.chat.open();
    if (!offen) {
      return;
    }
    try {
      await this.chat.addParticipants(offen.id, userIds, groupIds);
      this.teilnehmer.set(await this.chat.participants(offen.id));
      this.suche.set('');
      this.fehler.set('');
    } catch (e) {
      this.fehler.set(problem(e, 'Aufnehmen hat nicht geklappt.'));
    }
  }

  protected async herausnehmen(userId: string): Promise<void> {
    const offen = this.chat.open();
    if (!offen) {
      return;
    }
    await this.chat.removeParticipant(offen.id, userId);
    if (userId === this.me) {
      // Selbst gegangen: Fenster und Unterhaltung zu.
      this.fenster.set('keins');
      this.chat.close();
      return;
    }
    this.teilnehmer.set(await this.chat.participants(offen.id));
  }

  // ── Archivieren, Verlassen, Löschen ──────────────────────────────────

  protected async archivieren(): Promise<void> {
    const offen = this.chat.open();
    if (offen) {
      this.menuOpen.set(false);
      await this.chat.archive(offen.id);
    }
  }

  protected async wiederherstellen(conversation: Conversation): Promise<void> {
    this.menuOpen.set(false);
    await this.chat.unarchive(conversation.id);
  }

  protected async verlassen(): Promise<void> {
    const offen = this.chat.open();
    if (offen && this.me) {
      this.menuOpen.set(false);
      await this.chat.removeParticipant(offen.id, this.me);
      this.chat.close();
    }
  }

  protected loeschenFragen(): void {
    this.menuOpen.set(false);
    this.fenster.set('loeschen');
  }

  protected async loeschen(): Promise<void> {
    const offen = this.chat.open();
    if (!offen) {
      return;
    }
    try {
      await this.chat.remove(offen.id);
      this.fenster.set('keins');
    } catch (e) {
      this.fehler.set(problem(e, 'Löschen hat nicht geklappt.'));
    }
  }

  protected schliesseFenster(): void {
    this.fenster.set('keins');
    this.suche.set('');
  }
}

/**
 * Zerlegt einen Text in Stücke, damit „@Anna Krause" als Erwähnung
 * hervortritt. Geprüft wird gegen die bekannten Namen, längste zuerst --
 * sonst fände „@Anna" in „@Anna Krause" nur den Vornamen.
 */
function zerlegen(text: string, namen: string[]): Textteil[] {
  if (namen.length === 0 || !text.includes('@')) {
    return [{ text, erwaehnung: false }];
  }

  const teile: Textteil[] = [];
  let rest = text;
  while (rest.length > 0) {
    const at = rest.indexOf('@');
    if (at < 0) {
      teile.push({ text: rest, erwaehnung: false });
      break;
    }
    const name = namen.find((n) => rest.startsWith(n, at + 1));
    if (!name) {
      teile.push({ text: rest.slice(0, at + 1), erwaehnung: false });
      rest = rest.slice(at + 1);
      continue;
    }
    if (at > 0) {
      teile.push({ text: rest.slice(0, at), erwaehnung: false });
    }
    teile.push({ text: `@${name}`, erwaehnung: true });
    rest = rest.slice(at + 1 + name.length);
  }
  return teile;
}

/** Die Sätze aus einer abgewiesenen Anfrage, sonst ein eigener. */
function problem(e: unknown, sonst: string): string {
  const fehler = (e as { error?: { errors?: Record<string, string[]> } })?.error?.errors;
  const saetze = fehler ? Object.values(fehler).flat() : [];
  return saetze.length > 0 ? saetze.join(' ') : sonst;
}

/** Ein kurzer Auszug der Nachricht, auf die geantwortet wird. */
function zitatVon(original: ChatMessage | undefined): { id: string | null; author: string; text: string } {
  if (!original) {
    return { id: null, author: '', text: 'Antwort auf eine ältere Nachricht' };
  }
  const text = original.isRetracted
    ? 'zurückgezogen'
    : original.text.length > 80
      ? `${original.text.slice(0, 80)}…`
      : original.text;
  return { id: original.id, author: original.authorName, text };
}
