import { Component, input, output, signal } from '@angular/core';
import { Player, RandomDrawMode, RandomDrawState, Team } from '../../../../models/session.model';
import { ParticipantSelection, ParticipantSelectorComponent } from '../../../../shared/components/participant-selector/participant-selector.component';

export interface RandomDrawStartPayload {
  mode: RandomDrawMode;
  label: string;
  min: number;
  max: number;
  selection: ParticipantSelection;
}

@Component({
  selector: 'app-random-draw-host-panel',
  imports: [ParticipantSelectorComponent],
  templateUrl: './random-draw-host-panel.component.html',
  styleUrl: './random-draw-host-panel.component.scss'
})
export class RandomDrawHostPanelComponent {
  readonly draw = input<RandomDrawState | null>(null);
  readonly open = input(false);
  readonly players = input.required<Player[]>();
  readonly teams = input.required<Team[]>();
  readonly error = input<string | null>(null);
  readonly start = output<RandomDrawStartPayload>();
  readonly reveal = output<void>();
  readonly close = output<void>();

  protected readonly mode = signal<RandomDrawMode>('Reveal');
  protected readonly label = signal('');
  protected readonly min = signal(1);
  protected readonly max = signal(100);

  protected submitStart(selection: ParticipantSelection): void {
    this.start.emit({ mode: this.mode(), label: this.label(), min: this.min(), max: this.max(), selection });
  }
}
