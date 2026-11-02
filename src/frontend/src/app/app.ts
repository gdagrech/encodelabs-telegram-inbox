import { ChangeDetectionStrategy, Component } from '@angular/core';
import { InboxPageComponent } from './features/inbox/inbox-page.component';

@Component({
  selector: 'app-root',
  imports: [InboxPageComponent],
  template: '<app-inbox-page />',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class App {}
