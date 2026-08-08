import { Component, effect, input, output, signal } from '@angular/core';
import { StrawPollState } from '../../../../models/session.model';
import { UiCardComponent } from '../../../../shared/components/ui-card/ui-card.component';

@Component({
  selector: 'app-strawpoll-overlay',
  imports: [UiCardComponent],
  templateUrl: './strawpoll-overlay.component.html',
  styleUrl: './strawpoll-overlay.component.scss'
})
export class StrawPollOverlayComponent {
  readonly poll = input.required<StrawPollState>();
  readonly hasVoted = input.required<boolean>();
  readonly submitting = input(false);
  readonly error = input<string | null>(null);
  readonly voteSubmitted = output<string[]>();

  protected readonly selectedOptionIds = signal<string[]>([]);

  constructor() {
    // Vide le formulaire dès qu'un nouveau sondage démarre (ID différent).
    let lastPollId: number | null = null;
    effect(() => {
      const poll = this.poll();
      if (poll.id !== lastPollId) {
        lastPollId = poll.id;
        this.selectedOptionIds.set([]);
      }
    });
  }

  protected toggleOption(optionId: string): void {
    const poll = this.poll();
    this.selectedOptionIds.update((selected) => {
      if (selected.includes(optionId)) {
        return selected.filter((id) => id !== optionId);
      }
      if (!poll.allowMultipleVotes) {
        return [optionId];
      }
      return [...selected, optionId];
    });
  }

  protected submit(): void {
    if (this.selectedOptionIds().length === 0) {
      return;
    }
    this.voteSubmitted.emit(this.selectedOptionIds());
  }

  protected optionText(optionId: string): string {
    return this.poll().options.find((o) => o.id === optionId)?.text ?? '';
  }
}
