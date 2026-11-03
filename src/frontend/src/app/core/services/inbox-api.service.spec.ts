import { HttpErrorResponse } from '@angular/common/http';
import { describe, expect, it } from 'vitest';
import { describeRequestFailure, isReplyDeliveredButNotRecorded } from './inbox-api.service';

/**
 * Regression tests for the wording the operator reads when a request fails.
 *
 * Two defects came from this function. CR-010: the server's English prose leaked into a Spanish
 * interface. CR-025: nginx sits between the browser and the API and emits its own 502, which was
 * reported as "Telegram rejected the reply" even though Telegram was never involved.
 */
const FALLBACK = 'No se pudo enviar la respuesta a Telegram.';

/** A failure produced by the API itself: a ProblemDetails body. */
function apiFailure(status: number, problemDetails: Record<string, unknown>): HttpErrorResponse {
  return new HttpErrorResponse({ status, error: problemDetails });
}

/** A failure produced by a proxy in front of the API: an HTML body, no ProblemDetails. */
function proxyFailure(status: number): HttpErrorResponse {
  return new HttpErrorResponse({
    status,
    error: '<html><head><title>502 Bad Gateway</title></head><body>…</body></html>',
  });
}

describe('describeRequestFailure', () => {
  it('reports an unreachable server when the request never left the browser', () => {
    expect(describeRequestFailure(new HttpErrorResponse({ status: 0 }), FALLBACK)).toContain(
      'No se pudo contactar al servidor',
    );
  });

  it("quotes Telegram's own explanation when Telegram rejected the reply", () => {
    const message = describeRequestFailure(
      apiFailure(502, { title: 'The reply was not delivered', detail: 'Forbidden: bot was blocked by the user' }),
      FALLBACK,
    );

    expect(message).toContain('Telegram rechazó la respuesta');
    expect(message).toContain('bot was blocked by the user');
  });

  it('distinguishes an unreachable Telegram from a rejection, because only one is worth retrying', () => {
    const rejection = describeRequestFailure(apiFailure(502, { title: 'x', detail: 'Forbidden' }), FALLBACK);
    const unreachable = describeRequestFailure(apiFailure(504, { title: 'x', detail: 'unreachable' }), FALLBACK);

    expect(rejection).not.toEqual(unreachable);
    expect(unreachable).toContain('No se pudo contactar a Telegram');
  });

  /**
   * CR-025. nginx answers 502 with HTML when it cannot reach the API. Blaming Telegram sends the
   * operator to investigate the wrong system entirely.
   */
  it('blames the API, not Telegram, when a 502 did not come from the API', () => {
    const message = describeRequestFailure(proxyFailure(502), FALLBACK);

    expect(message).not.toContain('Telegram');
    expect(message).toContain('API');
  });

  it('blames the API for a proxy 504 as well', () => {
    expect(describeRequestFailure(proxyFailure(504), FALLBACK)).not.toContain('Telegram');
  });

  /**
   * CR-020. The contact already has the message, so the wording has to stop a retry rather than
   * invite one.
   */
  it('tells the operator not to resend a reply that was delivered but not recorded', () => {
    const message = describeRequestFailure(
      apiFailure(500, {
        title: 'The reply was delivered but not recorded',
        type: 'https://encodelabs.inbox/problems/reply-delivered-but-not-recorded',
        detail: 'ignored',
      }),
      FALLBACK,
    );

    expect(message).toContain('se entregó');
    expect(message).toContain('No la reenvíes');
  });

  it('states the length rule on a rejected reply', () => {
    expect(describeRequestFailure(apiFailure(400, { title: 'Invalid request' }), FALLBACK)).toContain('4096');
  });

  it('asks the operator to retry when the broker is unavailable', () => {
    expect(describeRequestFailure(apiFailure(503, { title: 'unavailable' }), FALLBACK)).toContain('Reintentá');
  });

  it('falls back to the caller-supplied sentence for anything unrecognised', () => {
    expect(describeRequestFailure(apiFailure(418, { title: "I'm a teapot" }), FALLBACK)).toEqual(FALLBACK);
  });

  it('falls back when the failure is not an HTTP response at all', () => {
    expect(describeRequestFailure(new Error('boom'), FALLBACK)).toEqual(FALLBACK);
  });

  it('never echoes raw English prose from the API for failures it words itself', () => {
    const message = describeRequestFailure(
      apiFailure(503, { title: 'The message broker is unavailable', detail: 'Please retry.' }),
      FALLBACK,
    );

    expect(message).not.toContain('Please retry');
  });
});

describe('isReplyDeliveredButNotRecorded', () => {
  it('recognises only the marked response', () => {
    const marked = apiFailure(500, {
      title: 'x',
      type: 'https://encodelabs.inbox/problems/reply-delivered-but-not-recorded',
    });

    expect(isReplyDeliveredButNotRecorded(marked)).toBe(true);
    expect(isReplyDeliveredButNotRecorded(apiFailure(500, { title: 'x' }))).toBe(false);
    expect(isReplyDeliveredButNotRecorded(proxyFailure(502))).toBe(false);
    expect(isReplyDeliveredButNotRecorded(new Error('boom'))).toBe(false);
  });
});
