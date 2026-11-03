import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { beforeEach, describe, expect, it } from 'vitest';
import { InboxStore, ReplyOutcome } from '../../core/services/inbox-store';
import { MessageComposerComponent } from './message-composer.component';

/**
 * Regression tests for CR-019: an unsent draft used to survive a change of conversation.
 *
 * The composer is not re-created when the operator switches contacts —it lives inside the same
 * template branch— so a plain signal kept the previous contact's text and the next Enter would have
 * delivered it to the wrong person. In a messaging inbox that is the worst possible defect, and the
 * challenge rules out editing or deleting a sent message, so it is irreversible.
 */
describe('MessageComposerComponent', () => {
  /** Minimal stand-in for the store: only what the composer reads and calls. */
  class InboxStoreStub {
    readonly selectedContactId = signal<string | null>(null);
    readonly isSendingReply = signal(false);
    replyOutcome: ReplyOutcome = 'stored';
    sentTexts: string[] = [];

    sendReply(replyText: string): Promise<ReplyOutcome> {
      this.sentTexts.push(replyText);
      return Promise.resolve(this.replyOutcome);
    }
  }

  let inboxStore: InboxStoreStub;
  let fixture: ComponentFixture<MessageComposerComponent>;

  beforeEach(async () => {
    inboxStore = new InboxStoreStub();

    await TestBed.configureTestingModule({
      imports: [MessageComposerComponent],
      providers: [{ provide: InboxStore, useValue: inboxStore }],
    }).compileComponents();

    fixture = TestBed.createComponent(MessageComposerComponent);
    fixture.componentRef.setInput('contactDisplayName', 'Ana');
    inboxStore.selectedContactId.set('contacto-ana');
    fixture.detectChanges();
  });

  function replyBox(): HTMLTextAreaElement {
    return fixture.nativeElement.querySelector('textarea') as HTMLTextAreaElement;
  }

  function type(draftText: string): void {
    const textarea = replyBox();
    textarea.value = draftText;
    textarea.dispatchEvent(new Event('input'));
    fixture.detectChanges();
  }

  it('keeps the draft while the operator stays in the same conversation', () => {
    type('respuesta a medio escribir');
    fixture.detectChanges();

    expect(replyBox().value).toBe('respuesta a medio escribir');
  });

  /** The regression itself: Ana's text must not appear in Bruno's box. */
  it('discards the draft when the operator switches to another contact', () => {
    type('texto para Ana');

    inboxStore.selectedContactId.set('contacto-bruno');
    fixture.componentRef.setInput('contactDisplayName', 'Bruno');
    fixture.detectChanges();

    expect(replyBox().value).toBe('');
  });

  it('does not bring the draft back when returning to the first contact', () => {
    type('texto para Ana');

    inboxStore.selectedContactId.set('contacto-bruno');
    fixture.detectChanges();
    inboxStore.selectedContactId.set('contacto-ana');
    fixture.detectChanges();

    expect(replyBox().value).toBe('');
  });

  it('clears the box once the reply was stored', async () => {
    type('hola');
    await fixture.componentInstance['submitReply']();
    fixture.detectChanges();

    expect(inboxStore.sentTexts).toEqual(['hola']);
    expect(replyBox().value).toBe('');
  });

  it('keeps the text when the reply was never delivered, so it can be retried', async () => {
    inboxStore.replyOutcome = 'notSent';
    type('hola');
    await fixture.componentInstance['submitReply']();
    fixture.detectChanges();

    expect(replyBox().value).toBe('hola');
  });

  /**
   * CR-020. Telegram already delivered this one, so leaving the text in place would invite a retry
   * that reaches the contact twice.
   */
  it('clears the box when the reply was delivered but not recorded, to prevent a duplicate', async () => {
    inboxStore.replyOutcome = 'deliveredButNotRecorded';
    type('hola');
    await fixture.componentInstance['submitReply']();
    fixture.detectChanges();

    expect(replyBox().value).toBe('');
  });

  it('refuses to send whitespace', async () => {
    type('    ');
    await fixture.componentInstance['submitReply']();

    expect(inboxStore.sentTexts).toEqual([]);
  });

  it('trims the text before sending it', async () => {
    type('  hola  ');
    await fixture.componentInstance['submitReply']();

    expect(inboxStore.sentTexts).toEqual(['hola']);
  });

  it('does not send twice while a send is in flight', async () => {
    type('hola');
    inboxStore.isSendingReply.set(true);
    fixture.detectChanges();

    await fixture.componentInstance['submitReply']();

    expect(inboxStore.sentTexts).toEqual([]);
  });
});
