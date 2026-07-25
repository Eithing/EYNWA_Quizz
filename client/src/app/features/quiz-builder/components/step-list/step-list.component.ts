import { Component, input, output } from '@angular/core';
import { STEP_TYPE_CATALOG } from '../../../../models/quiz-config.model';
import { UiBadgeComponent } from '../../../../shared/components/ui-badge/ui-badge.component';
import { QuizStepDraft } from '../../models/quiz-step-draft.model';

@Component({
  selector: 'app-step-list',
  imports: [UiBadgeComponent],
  templateUrl: './step-list.component.html',
  styleUrl: './step-list.component.scss'
})
export class StepListComponent {
  readonly steps = input.required<QuizStepDraft[]>();
  readonly selectedClientId = input<number | null>(null);

  readonly select = output<number>();
  readonly moveUp = output<number>();
  readonly moveDown = output<number>();
  readonly remove = output<number>();

  protected labelFor(type: QuizStepDraft['type']): string {
    return STEP_TYPE_CATALOG.find((meta) => meta.type === type)?.label ?? type;
  }
}
