import { ChangeDetectionStrategy, Component, computed, inject, input, linkedSignal } from '@angular/core';
import { InboxStore } from '../../core/services/inbox-store';

/**
 * The reply box. Unlike the other inbox components this one talks to the store directly, because it
 * owns the draft text and must know whether Telegram accepted the message before clearing it.
 */
@Component({
  selector: 'app-message-composer',
  templateUrl: './message-composer.component.html',
  styleUrl: './message-composer.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class MessageComposerComponent {
  private readonly inboxStore = inject(InboxStore);

  readonly contactDisplayName = input.required<string>();

  /**
   * The draft, tied to the contact it was written for. This component is not re-created when the
   * operator switches conversations —it lives inside the same template branch— so a plain signal
   * would carry one contact's unsent text into the next contact's box and deliver it to the wrong
   * person. Keying it to the selected contact discards the draft when the conversation changes.
   */
  protected readonly replyText = linkedSignal<string | null, string>({
    source: () => this.inboxStore.selectedContactId(),
    computation: () => '',
  });

  protected readonly isSendingReply = this.inboxStore.isSendingReply;

  /** Blocks empty submissions and, while a send is in flight, double submissions. */
  protected readonly canSubmitReply = computed(
    () => this.replyText().trim().length > 0 && !this.isSendingReply(),
  );

  protected updateReplyText(changeEvent: Event): void {
    this.replyText.set((changeEvent.target as HTMLTextAreaElement).value);
  }

  protected handleKeydown(keyboardEvent: KeyboardEvent): void {
    if (keyboardEvent.key === 'Enter' && !keyboardEvent.shiftKey) {
      keyboardEvent.preventDefault();
      void this.submitReply();
    }
  }

  protected async submitReply(): Promise<void> {
    if (!this.canSubmitReply()) {
      return;
    }

    const replyOutcome = await this.inboxStore.sendReply(this.replyText().trim());

    // The text is kept only when retrying would help. If Telegram already delivered it, clearing
    // the box is what stops the operator from sending the contact a duplicate; the banner explains
    // what happened.
    if (replyOutcome !== 'notSent') {
      this.replyText.set('');
    }
  }
}
