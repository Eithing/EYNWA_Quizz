import { Component, output } from '@angular/core';
import { STEP_TYPE_CATALOG, StepType, StepTypeMeta } from '../../../../models/quiz-config.model';
import { UiBadgeComponent } from '../../../../shared/components/ui-badge/ui-badge.component';
import { UiCardComponent } from '../../../../shared/components/ui-card/ui-card.component';

@Component({
  selector: 'app-step-catalog',
  imports: [UiCardComponent, UiBadgeComponent],
  templateUrl: './step-catalog.component.html',
  styleUrl: './step-catalog.component.scss'
})
export class StepCatalogComponent {
  protected readonly catalog = STEP_TYPE_CATALOG;

  readonly stepTypeSelected = output<StepType>();

  protected select(meta: StepTypeMeta): void {
    this.stepTypeSelected.emit(meta.type);
  }
}
