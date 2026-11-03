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
 * ProblemDetails carrying a technical reason; the wording belongs here, so the interface stays in
 * one language instead of echoing the server's English prose.
 */
export function describeRequestFailure(requestFailure: unknown, fallbackMessage: string): string {
  if (!(requestFailure instanceof HttpErrorResponse)) {
    return fallbackMessage;
  }

  if (requestFailure.status === 0) {
    return 'No se pudo contactar al servidor. Verificá que la API esté levantada.';
  }

  const problemDetails = requestFailure.error as { detail?: string } | null;
  const technicalReason = problemDetails?.detail;

  if (isReplyDeliveredButNotRecorded(requestFailure)) {
    return 'La respuesta se entregó en Telegram pero no se pudo guardar. No la reenvíes: le llegaría dos veces. Actualizá la bandeja.';
  }

  switch (requestFailure.status) {
    case 400:
      // The only rejected input is the reply text, so the rule is worth stating outright.
      return 'La respuesta debe tener entre 1 y 4096 caracteres.';
    case 404:
      return 'La conversación ya no existe. Actualizá la bandeja.';
    case 502:
      // Telegram's own words are the actionable part: "bot was blocked by the user" tells the
      // operator that retrying is pointless.
      return technicalReason
        ? `Telegram rechazó la respuesta: ${technicalReason}`
        : 'Telegram rechazó la respuesta.';
    case 503:
      return 'El mensaje no pudo encolarse porque el sistema de mensajería no está disponible. Reintentá en unos segundos.';
    case 504:
      return 'No se pudo contactar a Telegram. La respuesta no se entregó.';
    default:
      return fallbackMessage;
  }
}

/** Identifies the response the API marks when Telegram delivered a reply the inbox could not store. */
const REPLY_DELIVERED_BUT_NOT_RECORDED_PROBLEM_TYPE =
  'https://encodelabs.inbox/problems/reply-delivered-but-not-recorded';

/**
 * Whether the contact already received the reply despite the error. The status code cannot answer
 * this —it is a plain 500— so the API marks the response and the client matches on that mark.
 */
export function isReplyDeliveredButNotRecorded(requestFailure: unknown): boolean {
  if (!(requestFailure instanceof HttpErrorResponse)) {
    return false;
  }

  const problemDetails = requestFailure.error as { type?: string } | null;
  return problemDetails?.type === REPLY_DELIVERED_BUT_NOT_RECORDED_PROBLEM_TYPE;
}
