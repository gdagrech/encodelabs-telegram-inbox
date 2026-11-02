import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import {
  ContactSummary,
  ConversationHistory,
  ConversationReadState,
  MessageView,
} from '../models/inbox.models';

/**
 * Single place that knows the HTTP shape of the inbox API. Paths are relative because the frontend
 * is served from the same origin as the API (nginx in Docker, the dev-server proxy locally).
 */
@Injectable({ providedIn: 'root' })
export class InboxApiService {
  private readonly httpClient = inject(HttpClient);

  listContacts(): Promise<ContactSummary[]> {
    return firstValueFrom(this.httpClient.get<ContactSummary[]>('/api/contacts'));
  }

  getConversation(contactId: string): Promise<ConversationHistory> {
    return firstValueFrom(
      this.httpClient.get<ConversationHistory>(`/api/contacts/${contactId}/messages`),
    );
  }

  sendReply(contactId: string, replyText: string): Promise<MessageView> {
    return firstValueFrom(
      this.httpClient.post<MessageView>(`/api/contacts/${contactId}/messages`, {
        text: replyText,
      }),
    );
  }

  markConversationAsRead(
    contactId: string,
    readUpToSequenceNumber: number,
  ): Promise<ConversationReadState> {
    return firstValueFrom(
      this.httpClient.post<ConversationReadState>(`/api/contacts/${contactId}/read-state`, {
        readUpToSequenceNumber,
      }),
    );
  }
}

/**
 * Turns a failed request into a sentence an operator can act on. The API answers with
 * ProblemDetails, whose <c>detail</c> already explains Telegram and broker failures precisely.
 */
export function describeRequestFailure(requestFailure: unknown, fallbackMessage: string): string {
  if (requestFailure instanceof HttpErrorResponse) {
    if (requestFailure.status === 0) {
      return 'No se pudo contactar al servidor. Verificá que la API esté levantada.';
    }

    const problemDetails = requestFailure.error as { detail?: string; title?: string } | null;
    const problemDescription = problemDetails?.detail ?? problemDetails?.title;
    if (problemDescription) {
      return problemDescription;
    }
  }

  return fallbackMessage;
}
