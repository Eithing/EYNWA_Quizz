import { Component, input, output, signal } from '@angular/core';
import { Player, StrawPollState, Team } from '../../../../models/session.model';
import { ParticipantSelection, ParticipantSelectorComponent } from '../../../../shared/components/participant-selector/participant-selector.component';

export interface StrawPollStartPayload {
  question: string;
  optionsText: string;
  allowMultiple: boolean;
  selection: ParticipantSelection;
}

@Component({
  selector: 'app-strawpoll-host-panel',
  imports: [ParticipantSelectorComponent],
  templateUrl: './strawpoll-host-panel.component.html',
  styleUrl: './strawpoll-host-panel.component.scss'
})
export class StrawPollHostPanelComponent {
  readonly poll = input<StrawPollState | null>(null);
  readonly open = input(false);
  readonly players = input.required<Player[]>();
  readonly teams = input.required<Team[]>();
  readonly error = input<string | null>(null);
  readonly start = output<StrawPollStartPayload>();
  readonly reveal = output<void>();
  readonly close = output<void>();

  protected readonly question = signal('');
  protected readonly optionsText = signal('');
  protected readonly allowMultiple = signal(false);

  protected optionText(poll: StrawPollState, optionId: string): string {
    return poll.options.find((o) => o.id === optionId)?.text ?? '';
  }

  protected submitStart(selection: ParticipantSelection): void {
    this.start.emit({ question: this.question(), optionsText: this.optionsText(), allowMultiple: this.allowMultiple(), selection });
  }
}
