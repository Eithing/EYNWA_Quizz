import { Component, input } from '@angular/core';

export type BadgeTone = 'neutral' | 'accent' | 'success' | 'warning' | 'danger';

@Component({
  selector: 'app-ui-badge',
  imports: [],
  templateUrl: './ui-badge.component.html',
  styleUrl: './ui-badge.component.scss'
})
export class UiBadgeComponent {
  readonly tone = input<BadgeTone>('neutral');
}
