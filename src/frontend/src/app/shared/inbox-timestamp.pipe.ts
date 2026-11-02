import { Pipe, PipeTransform } from '@angular/core';

const timeFormatter = new Intl.DateTimeFormat('es-AR', { hour: '2-digit', minute: '2-digit' });
const dateFormatter = new Intl.DateTimeFormat('es-AR', { day: '2-digit', month: '2-digit' });

/**
 * Formats an ISO timestamp for the inbox: the time for today's messages, the date for older ones,
 * which is what makes a long conversation readable without showing a full timestamp on every line.
 */
@Pipe({ name: 'inboxTimestamp' })
export class InboxTimestampPipe implements PipeTransform {
  transform(isoTimestamp: string): string {
    const timestamp = new Date(isoTimestamp);
    if (Number.isNaN(timestamp.getTime())) {
      return '';
    }

    return isToday(timestamp) ? timeFormatter.format(timestamp) : dateFormatter.format(timestamp);
  }
}

function isToday(timestamp: Date): boolean {
  const now = new Date();

  return (
    timestamp.getFullYear() === now.getFullYear() &&
    timestamp.getMonth() === now.getMonth() &&
    timestamp.getDate() === now.getDate()
  );
}
