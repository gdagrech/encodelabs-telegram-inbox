import { ComponentFixture, TestBed } from '@angular/core/testing';
import { beforeEach, describe, expect, it } from 'vitest';
import { ContactSummary } from '../../core/models/inbox.models';
import { ContactListComponent } from './contact-list.component';

/**
 * Regression tests for CR-022: a failed load used to render the reassuring empty state.
 *
 * "Todavía no hay conversaciones" is a statement of fact about the data. Showing it when the request
 * failed puts two contradictory claims on the same screen —an error banner above, a calm "nothing
 * here" below— and the operator cannot tell which to believe. It is the same class of defect as
 * CR-017, which we had already fixed once.
 */
describe('ContactListComponent', () => {
  let fixture: ComponentFixture<ContactListComponent>;

  const anyContact: ContactSummary = {
    contactId: 'contacto-ana',
    displayName: 'Ana',
    username: null,
    unreadMessageCount: 2,
    lastMessagePreview: 'hola',
    lastMessageDirection: 'Incoming',
    lastActivityAtUtc: '2026-11-03T12:00:00+00:00',
  };

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [ContactListComponent] }).compileComponents();
    fixture = TestBed.createComponent(ContactListComponent);
    fixture.componentRef.setInput('selectedContactId', null);
  });

  function render(state: {
    contacts: readonly ContactSummary[];
    isLoading: boolean;
    hasLoaded: boolean;
  }): string {
    fixture.componentRef.setInput('contacts', state.contacts);
    fixture.componentRef.setInput('isLoading', state.isLoading);
    fixture.componentRef.setInput('hasLoaded', state.hasLoaded);
    fixture.detectChanges();

    return (fixture.nativeElement as HTMLElement).textContent ?? '';
  }

  it('shows a loading state on the first fetch', () => {
    const text = render({ contacts: [], isLoading: true, hasLoaded: false });

    expect(text).toContain('Cargando contactos');
  });

  it('invites the operator to write to the bot when the inbox is genuinely empty', () => {
    const text = render({ contacts: [], isLoading: false, hasLoaded: true });

    expect(text).toContain('Todavía no hay conversaciones');
  });

  /** The regression itself. */
  it('never claims the inbox is empty when the listing could not be loaded', () => {
    const text = render({ contacts: [], isLoading: false, hasLoaded: false });

    expect(text).not.toContain('Todavía no hay conversaciones');
    expect(text).toContain('No se pudo cargar');
  });

  it('keeps showing the contacts it already has while refreshing', () => {
    const text = render({ contacts: [anyContact], isLoading: true, hasLoaded: true });

    expect(text).toContain('Ana');
    expect(text).toContain('Actualizando');
  });

  it('marks the operator’s own last message in the preview', () => {
    const component = fixture.componentInstance;

    expect(component.buildPreviewLabel({ ...anyContact, lastMessageDirection: 'Outgoing' })).toBe('Vos: hola');
    expect(component.buildPreviewLabel(anyContact)).toBe('hola');
    expect(component.buildPreviewLabel({ ...anyContact, lastMessagePreview: null })).toContain('Sin mensajes');
  });

  it('falls back to a question mark when a display name has no usable first character', () => {
    const component = fixture.componentInstance;

    expect(component.buildInitials('Ana')).toBe('A');
    expect(component.buildInitials('@ana')).toBe('A');
    expect(component.buildInitials('   ')).toBe('?');
    expect(component.buildInitials('')).toBe('?');
  });
});
