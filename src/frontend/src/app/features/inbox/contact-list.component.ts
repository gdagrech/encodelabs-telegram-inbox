import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { ContactSummary } from '../../core/models/inbox.models';
import { InboxTimestampPipe } from '../../shared/inbox-timestamp.pipe';

/** Side panel: every known contact, most recent activity first. */
@Component({
  selector: 'app-contact-list',
  imports: [InboxTimestampPipe],
  templateUrl: './contact-list.component.html',
  styleUrl: './contact-list.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ContactListComponent {
  readonly contacts = input.required<readonly ContactSummary[]>();
  readonly selectedContactId = input.required<string | null>();
  readonly isLoading = input.required<boolean>();
  readonly hasLoaded = input.required<boolean>();

  readonly contactSelected = output<string>();

  /** Distinguishes the operator's own last message from the contact's in the preview line. */
  buildPreviewLabel(contact: ContactSummary): string {
    if (contact.lastMessagePreview === null) {
      return 'Sin mensajes todavía';
    }

    return contact.lastMessageDirection === 'Outgoing'
      ? `Vos: ${contact.lastMessagePreview}`
      : contact.lastMessagePreview;
  }

  buildContactAriaLabel(contact: ContactSummary): string {
    const unreadDescription =
      contact.unreadMessageCount > 0
        ? `, ${contact.unreadMessageCount} mensajes sin leer`
        : ', sin mensajes pendientes';

    return `Conversación con ${contact.displayName}${unreadDescription}`;
  }

  buildInitials(displayName: string): string {
    const firstVisibleCharacter = displayName.replace(/^@/, '').trim().charAt(0);

    return firstVisibleCharacter === '' ? '?' : firstVisibleCharacter.toUpperCase();
  }
}
