import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject } from '@angular/core';
import { RealtimeConnectionState } from '../../core/models/inbox.models';
import { InboxStore } from '../../core/services/inbox-store';
import { ContactListComponent } from './contact-list.component';
import { ConversationPanelComponent } from './conversation-panel.component';

const connectionStateLabels: Record<RealtimeConnectionState, string> = {
  connecting: 'Conectando…',
  connected: 'En línea',
  reconnecting: 'Reconectando…',
  disconnected: 'Sin tiempo real',
};

/** Two-column desktop shell: contacts on the left, the selected conversation in the centre. */
@Component({
  selector: 'app-inbox-page',
  imports: [ContactListComponent, ConversationPanelComponent],
  templateUrl: './inbox-page.component.html',
  styleUrl: './inbox-page.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class InboxPageComponent implements OnInit {
  private readonly inboxStore = inject(InboxStore);

  protected readonly contacts = this.inboxStore.contacts;
  protected readonly selectedContactId = this.inboxStore.selectedContactId;
  protected readonly selectedContact = this.inboxStore.selectedContact;
  protected readonly conversationMessages = this.inboxStore.conversationMessages;
  protected readonly isLoadingContacts = this.inboxStore.isLoadingContacts;
  protected readonly isLoadingConversation = this.inboxStore.isLoadingConversation;
  protected readonly hasLoadedContacts = this.inboxStore.hasLoadedContacts;
  protected readonly hasLoadedConversation = this.inboxStore.hasLoadedConversation;
  protected readonly errorMessage = this.inboxStore.errorMessage;
  protected readonly totalUnreadMessageCount = this.inboxStore.totalUnreadMessageCount;

  protected readonly connectionStateLabel = computed(
    () => connectionStateLabels[this.inboxStore.connectionState()],
  );

  protected readonly isRealtimeConnected = computed(
    () => this.inboxStore.connectionState() === 'connected',
  );

  constructor() {
    // Closing the hub connection on teardown avoids a leaked socket during development reloads.
    inject(DestroyRef).onDestroy(() => void this.inboxStore.stop());
  }

  ngOnInit(): void {
    void this.inboxStore.initialize();
  }

  protected selectContact(contactId: string): void {
    void this.inboxStore.selectContact(contactId);
  }

  protected retryLoad(): void {
    void this.inboxStore.reloadInboxState();
  }

  protected dismissError(): void {
    this.inboxStore.dismissError();
  }
}
