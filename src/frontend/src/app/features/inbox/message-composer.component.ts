import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
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

  protected readonly replyText = signal('');
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

    const wasDeliveredToTelegram = await this.inboxStore.sendReply(this.replyText().trim());

    // Keep the text on failure so the operator can retry without retyping it.
    if (wasDeliveredToTelegram) {
      this.replyText.set('');
    }
  }
}
