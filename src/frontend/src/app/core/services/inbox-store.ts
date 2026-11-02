import { Injectable, computed, inject, signal } from '@angular/core';
import {
  ContactSummary,
  ConversationHistory,
  ConversationReadState,
  MessageAddedNotification,
  MessageView,
} from '../models/inbox.models';
import { InboxApiService, describeRequestFailure } from './inbox-api.service';
import { InboxRealtimeService } from './inbox-realtime.service';

/**
 * The single source of truth for what the inbox shows. Components read signals from here and call
 * its methods; they never talk to the API or the hub directly, which keeps one state machine
 * instead of several views of the same data.
 */
@Injectable({ providedIn: 'root' })
export class InboxStore {
  private readonly inboxApiService = inject(InboxApiService);
  private readonly inboxRealtimeService = inject(InboxRealtimeService);

  private readonly contactsState = signal<readonly ContactSummary[]>([]);
  private readonly selectedContactIdState = signal<string | null>(null);
  private readonly conversationMessagesState = signal<readonly MessageView[]>([]);
  private readonly isLoadingContactsState = signal(false);
  private readonly isLoadingConversationState = signal(false);
  private readonly isSendingReplyState = signal(false);
  private readonly hasLoadedContactsState = signal(false);
  private readonly errorMessageState = signal<string | null>(null);

  /**
   * Incremented on every conversation load. A response whose token no longer matches belongs to a
   * conversation the operator has already navigated away from and is discarded.
   */
  private conversationRequestSequence = 0;

  readonly contacts = this.contactsState.asReadonly();
  readonly selectedContactId = this.selectedContactIdState.asReadonly();
  readonly conversationMessages = this.conversationMessagesState.asReadonly();
  readonly isLoadingContacts = this.isLoadingContactsState.asReadonly();
  readonly isLoadingConversation = this.isLoadingConversationState.asReadonly();
  readonly isSendingReply = this.isSendingReplyState.asReadonly();
  readonly hasLoadedContacts = this.hasLoadedContactsState.asReadonly();
  readonly errorMessage = this.errorMessageState.asReadonly();
  readonly connectionState = this.inboxRealtimeService.connectionState.asReadonly();

  readonly selectedContact = computed<ContactSummary | null>(() => {
    const selectedContactId = this.selectedContactIdState();
    if (selectedContactId === null) {
      return null;
    }

    return this.contactsState().find((contact) => contact.contactId === selectedContactId) ?? null;
  });

  readonly totalUnreadMessageCount = computed(() =>
    this.contactsState().reduce((runningTotal, contact) => runningTotal + contact.unreadMessageCount, 0),
  );

  async initialize(): Promise<void> {
    await this.inboxRealtimeService.start({
      onMessageAdded: (notification) => this.applyMessageAdded(notification),
      onConversationReadStateChanged: (readState) => this.applyConversationReadState(readState),
      // Fires on first connect and after every reconnect: anything committed while the socket was
      // down was never pushed, so the client refetches rather than trusting stale state.
      onConnectionEstablished: () => void this.reloadInboxState(),
    });

    if (this.connectionState() !== 'connected') {
      // The realtime channel is unavailable; the inbox must still render what the API knows.
      await this.reloadInboxState();
    }
  }

  async stop(): Promise<void> {
    await this.inboxRealtimeService.stop();
  }

  /** Refetches the contact list and, if one is open, the conversation on screen. */
  async reloadInboxState(): Promise<void> {
    await this.loadContacts();

    const selectedContactId = this.selectedContactIdState();
    if (selectedContactId === null) {
      return;
    }

    // The open contact may be gone, for instance after the database was reset. Forgetting the
    // selection is the correct recovery: asking for its history would raise an error about a
    // conversation the operator can no longer act on, contradicting the empty state beside it.
    const isContactStillListed = this.contactsState()
      .some((contact) => contact.contactId === selectedContactId);

    if (!isContactStillListed) {
      this.selectedContactIdState.set(null);
      this.conversationMessagesState.set([]);
      return;
    }

    await this.loadConversation(selectedContactId);
  }

  async selectContact(contactId: string): Promise<void> {
    if (this.selectedContactIdState() === contactId) {
      return;
    }

    this.selectedContactIdState.set(contactId);
    this.errorMessageState.set(null);
    // Drop the previous conversation immediately: showing one contact's messages under another
    // contact's header, even for a few hundred milliseconds, is worse than showing a loading state.
    this.conversationMessagesState.set([]);
    await this.loadConversation(contactId);
  }

  /**
   * Sends a reply. Returns whether Telegram accepted it, so the composer only clears the text it
   * actually managed to deliver.
   */
  async sendReply(replyText: string): Promise<boolean> {
    const selectedContactId = this.selectedContactIdState();
    if (selectedContactId === null || this.isSendingReplyState()) {
      return false;
    }

    this.isSendingReplyState.set(true);
    this.errorMessageState.set(null);

    try {
      const sentMessage = await this.inboxApiService.sendReply(selectedContactId, replyText);
      this.appendMessageIfAbsent(selectedContactId, sentMessage);
      return true;
    } catch (sendFailure) {
      this.errorMessageState.set(
        describeRequestFailure(sendFailure, 'No se pudo enviar la respuesta a Telegram.'),
      );
      return false;
    } finally {
      this.isSendingReplyState.set(false);
    }
  }

  dismissError(): void {
    this.errorMessageState.set(null);
  }

  private async loadContacts(): Promise<void> {
    this.isLoadingContactsState.set(true);
    try {
      const contacts = await this.inboxApiService.listContacts();
      this.contactsState.set(sortContactsByLastActivity(contacts));
      this.hasLoadedContactsState.set(true);
    } catch (loadFailure) {
      this.errorMessageState.set(
        describeRequestFailure(loadFailure, 'No se pudo cargar el listado de contactos.'),
      );
    } finally {
      this.isLoadingContactsState.set(false);
    }
  }

  private async loadConversation(contactId: string): Promise<void> {
    const requestToken = ++this.conversationRequestSequence;
    this.isLoadingConversationState.set(true);

    try {
      const conversationHistory: ConversationHistory =
        await this.inboxApiService.getConversation(contactId);

      if (requestToken !== this.conversationRequestSequence) {
        return;
      }

      // Merge rather than replace: a message pushed over SignalR while this request was in flight
      // is not in the fetched history, and overwriting would make it vanish until the next refresh.
      this.conversationMessagesState.update((alreadyShownMessages) =>
        mergeMessagesBySequence(alreadyShownMessages, conversationHistory.messages),
      );

      await this.markConversationAsRead(contactId, conversationHistory.highestSequenceNumber);
    } catch (loadFailure) {
      if (requestToken !== this.conversationRequestSequence) {
        return;
      }

      this.errorMessageState.set(
        describeRequestFailure(loadFailure, 'No se pudo cargar la conversación.'),
      );
    } finally {
      if (requestToken === this.conversationRequestSequence) {
        this.isLoadingConversationState.set(false);
      }
    }
  }

  /**
   * Asks the server to mark everything up to the highest sequence number this client has received.
   * Messages that arrive while the request is in flight carry a higher number and stay unread.
   */
  private async markConversationAsRead(
    contactId: string,
    readUpToSequenceNumber: number,
  ): Promise<void> {
    if (readUpToSequenceNumber <= 0) {
      return;
    }

    try {
      const readState = await this.inboxApiService.markConversationAsRead(
        contactId,
        readUpToSequenceNumber,
      );

      this.applyConversationReadState(readState);
    } catch (markAsReadFailure) {
      this.errorMessageState.set(
        describeRequestFailure(
          markAsReadFailure,
          'No se pudieron marcar los mensajes como leídos. El contador puede quedar desactualizado.',
        ),
      );
    }
  }

  private applyMessageAdded(notification: MessageAddedNotification): void {
    this.upsertContactSummary(notification.contactSummary);

    const selectedContactId = this.selectedContactIdState();
    if (selectedContactId !== notification.contactSummary.contactId) {
      return;
    }

    const wasAppended = this.appendMessageIfAbsent(selectedContactId, notification.message);

    // The operator has the conversation open, so an incoming message is seen as it arrives.
    if (wasAppended && notification.message.direction === 'Incoming') {
      void this.markConversationAsRead(selectedContactId, notification.message.sequenceNumber);
    }
  }

  private applyConversationReadState(readState: ConversationReadState): void {
    this.contactsState.update((contacts) =>
      contacts.map((contact) =>
        contact.contactId === readState.contactId
          ? { ...contact, unreadMessageCount: readState.unreadMessageCount }
          : contact,
      ),
    );

    if (this.selectedContactIdState() !== readState.contactId) {
      return;
    }

    this.conversationMessagesState.update((messages) =>
      messages.map((message) =>
        message.direction === 'Incoming' &&
        !message.isRead &&
        message.sequenceNumber <= readState.readUpToSequenceNumber
          ? { ...message, isRead: true }
          : message,
      ),
    );
  }

  /**
   * Adds a message unless it is already on screen. Both the HTTP response of a send and the
   * broadcast that follows it carry the same message, and a reconnect can replay an event, so
   * de-duplication by identifier is what keeps the conversation from showing doubles.
   */
  private appendMessageIfAbsent(contactId: string, message: MessageView): boolean {
    if (this.selectedContactIdState() !== contactId) {
      return false;
    }

    let wasAppended = false;
    this.conversationMessagesState.update((messages) => {
      if (messages.some((existingMessage) => existingMessage.messageId === message.messageId)) {
        return messages;
      }

      wasAppended = true;
      return [...messages, message].sort(
        (firstMessage, secondMessage) => firstMessage.sequenceNumber - secondMessage.sequenceNumber,
      );
    });

    return wasAppended;
  }

  private upsertContactSummary(contactSummary: ContactSummary): void {
    this.contactsState.update((contacts) => {
      const remainingContacts = contacts.filter(
        (contact) => contact.contactId !== contactSummary.contactId,
      );

      return sortContactsByLastActivity([...remainingContacts, contactSummary]);
    });
  }
}

/** Unions two message sets by identifier and restores the server's arrival order. */
function mergeMessagesBySequence(
  alreadyShownMessages: readonly MessageView[],
  fetchedMessages: readonly MessageView[],
): readonly MessageView[] {
  const messagesByIdentifier = new Map<string, MessageView>();

  for (const message of [...alreadyShownMessages, ...fetchedMessages]) {
    // The fetched copy wins: it carries the read state as the database has it.
    messagesByIdentifier.set(message.messageId, message);
  }

  return [...messagesByIdentifier.values()].sort(
    (firstMessage, secondMessage) => firstMessage.sequenceNumber - secondMessage.sequenceNumber,
  );
}

function sortContactsByLastActivity(contacts: readonly ContactSummary[]): readonly ContactSummary[] {
  return [...contacts].sort(
    (firstContact, secondContact) =>
      Date.parse(secondContact.lastActivityAtUtc) - Date.parse(firstContact.lastActivityAtUtc),
  );
}
