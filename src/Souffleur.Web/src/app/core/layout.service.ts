import { Injectable, computed, signal } from '@angular/core';

/**
 * Der Zustand der beiden Seitenleisten. Links wird gehandelt, rechts geredet,
 * in der Mitte informiert.
 *
 * Angepinnt steht eine Leiste fest neben der Mitte und nimmt ihr Platz.
 * Nicht angepinnt bleibt nur ihr schmaler Streifen stehen; aufgeklappt legt
 * sie sich über die Mitte und geht wieder zu, sobald man etwas getan hat
 * oder daneben klickt -- wie ein Menü. Der Pin ist also die Wahl zwischen
 * Platz und Überblick, und er überlebt den Neustart. Ob eine nicht
 * angepinnte Leiste gerade offen ist, wird absichtlich nicht gemerkt: Ein
 * Menü, das beim Start schon über dem Inhalt liegt, wäre im Weg.
 *
 * Ein zugeklappter Streifen verhält sich immer gleich: Die Maus darauf klappt
 * die Leiste als Überlagerung auf, auch wenn sie angepinnt ist. Sonst blieb
 * eine angepinnte, zugeklappte Leiste stumm -- rechts war das sogar der
 * Startzustand. Der Pin entscheidet nur, wie die Leiste offen steht: fest
 * neben der Mitte oder darüber. „Peek“ ist dieses kurze Hereinschauen.
 */
@Injectable({ providedIn: 'root' })
export class LayoutService {
  readonly actionsPinned = signal(this.restore('actions-pinned', true));
  readonly actionsOpen = signal(this.actionsPinned() && this.restore('actions-open', true));
  readonly actionsPeek = signal(false);
  /** Fest neben der Mitte: angepinnt, offen und nicht nur hereingeschaut. */
  readonly actionsDocked = computed(
    () => this.actionsPinned() && this.actionsOpen() && !this.actionsPeek(),
  );

  readonly chatPinned = signal(this.restore('chat-pinned', true));
  readonly chatOpen = signal(this.chatPinned() && this.restore('chat-open', false));
  readonly chatPeek = signal(false);
  readonly chatDocked = computed(() => this.chatPinned() && this.chatOpen() && !this.chatPeek());

  toggleActions(): void {
    // Ein Klick aus dem Hereinschauen heraus klappt richtig auf.
    if (this.actionsPeek()) {
      this.actionsPeek.set(false);
      this.rememberActionsOpen();
      return;
    }
    this.actionsOpen.update((open) => !open);
    this.rememberActionsOpen();
  }

  /** Beim Hereinfahren mit der Maus: Das Menü kommt, bevor man klickt. */
  openActionsOverlay(): void {
    if (!this.actionsOpen()) {
      this.actionsPeek.set(this.actionsPinned());
      this.actionsOpen.set(true);
    }
  }

  /** Nach einer Aktion oder einem Klick daneben: Das Menü hat seinen Dienst getan. */
  closeActionsOverlay(): void {
    if (!this.actionsPinned() || this.actionsPeek()) {
      this.actionsOpen.set(false);
      this.actionsPeek.set(false);
    }
  }

  togglePin(): void {
    this.actionsPinned.update((pinned) => !pinned);
    this.persist('actions-pinned', this.actionsPinned());
    this.actionsPeek.set(false);
    // Anpinnen heißt: offen und fest. Lösen heißt: Das Menü geht zu, bis man
    // es braucht.
    this.actionsOpen.set(this.actionsPinned());
    this.rememberActionsOpen();
  }

  toggleChat(): void {
    if (this.chatPeek()) {
      this.chatPeek.set(false);
      this.rememberChatOpen();
      return;
    }
    this.chatOpen.update((open) => !open);
    this.rememberChatOpen();
  }

  openChatOverlay(): void {
    if (!this.chatOpen()) {
      this.chatPeek.set(this.chatPinned());
      this.chatOpen.set(true);
    }
  }

  closeChatOverlay(): void {
    if (!this.chatPinned() || this.chatPeek()) {
      this.chatOpen.set(false);
      this.chatPeek.set(false);
    }
  }

  /** Rechts dasselbe wie links. */
  toggleChatPin(): void {
    this.chatPinned.update((pinned) => !pinned);
    this.persist('chat-pinned', this.chatPinned());
    this.chatPeek.set(false);
    this.chatOpen.set(this.chatPinned());
    this.rememberChatOpen();
  }

  private rememberActionsOpen(): void {
    if (this.actionsPinned()) {
      this.persist('actions-open', this.actionsOpen());
    }
  }

  private rememberChatOpen(): void {
    if (this.chatPinned()) {
      this.persist('chat-open', this.chatOpen());
    }
  }

  private restore(key: string, fallback: boolean): boolean {
    try {
      const stored = localStorage.getItem(`souffleur.${key}`);
      return stored === null ? fallback : stored === 'true';
    } catch {
      return fallback;
    }
  }

  private persist(key: string, value: boolean): void {
    try {
      localStorage.setItem(`souffleur.${key}`, String(value));
    } catch {
      /* nicht weiter schlimm */
    }
  }
}
