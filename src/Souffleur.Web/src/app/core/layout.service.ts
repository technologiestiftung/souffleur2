import { Injectable, signal } from '@angular/core';

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
 */
@Injectable({ providedIn: 'root' })
export class LayoutService {
  readonly actionsPinned = signal(this.restore('actions-pinned', true));
  readonly actionsOpen = signal(this.actionsPinned() && this.restore('actions-open', true));

  readonly chatPinned = signal(this.restore('chat-pinned', true));
  readonly chatOpen = signal(this.chatPinned() && this.restore('chat-open', false));

  toggleActions(): void {
    this.actionsOpen.update((open) => !open);
    this.rememberActionsOpen();
  }

  /** Beim Hereinfahren mit der Maus: Das Menü kommt, bevor man klickt. */
  openActionsOverlay(): void {
    if (!this.actionsPinned()) {
      this.actionsOpen.set(true);
    }
  }

  /** Nach einer Aktion oder einem Klick daneben: Das Menü hat seinen Dienst getan. */
  closeActionsOverlay(): void {
    if (!this.actionsPinned()) {
      this.actionsOpen.set(false);
    }
  }

  togglePin(): void {
    this.actionsPinned.update((pinned) => !pinned);
    this.persist('actions-pinned', this.actionsPinned());
    // Anpinnen heißt: offen und fest. Lösen heißt: Das Menü geht zu, bis man
    // es braucht.
    this.actionsOpen.set(this.actionsPinned());
    this.rememberActionsOpen();
  }

  toggleChat(): void {
    this.chatOpen.update((open) => !open);
    this.rememberChatOpen();
  }

  openChatOverlay(): void {
    if (!this.chatPinned()) {
      this.chatOpen.set(true);
    }
  }

  closeChatOverlay(): void {
    if (!this.chatPinned()) {
      this.chatOpen.set(false);
    }
  }

  /** Rechts dasselbe wie links. */
  toggleChatPin(): void {
    this.chatPinned.update((pinned) => !pinned);
    this.persist('chat-pinned', this.chatPinned());
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
