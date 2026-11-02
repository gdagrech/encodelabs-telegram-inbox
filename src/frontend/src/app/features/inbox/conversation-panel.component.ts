import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  afterRenderEffect,
  input,
  viewChild,
} from '@angular/core';
import { ContactSummary, MessageView } from '../../core/models/inbox.models';
import { InboxTimestampPipe } from '../../shared/inbox-timestamp.pipe';
import { MessageComposerComponent } from './message-composer.component';

/** Centre panel: the selected conversation and the reply box. */
@Component({
  selector: 'app-conversation-panel',
  imports: [InboxTimestampPipe, MessageComposerComponent],
  templateUrl: './conversation-panel.component.html',
  styleUrl: './conversation-panel.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ConversationPanelComponent {
  readonly contact = input.required<ContactSummary | null>();
  readonly messages = input.required<readonly MessageView[]>();
  readonly isLoading = input.required<boolean>();

  private readonly messageScrollContainer =
    viewChild<ElementRef<HTMLDivElement>>('messageScrollContainer');

  constructor() {
    // Keep the newest message in view, including when one arrives over SignalR while reading.
    afterRenderEffect(() => {
      this.messages();
      const scrollContainer = this.messageScrollContainer()?.nativeElement;
      if (scrollContainer) {
        scrollContainer.scrollTop = scrollContainer.scrollHeight;
      }
    });
  }
}
