import { CdkDrag, CdkDragDrop, CdkDragHandle, CdkDropList } from '@angular/cdk/drag-drop';
import { Component, input, output } from '@angular/core';
import { UiBadgeComponent } from '../../../../shared/components/ui-badge/ui-badge.component';
import { RoundDraft } from '../../models/round-draft.model';

@Component({
  selector: 'app-round-list',
  imports: [UiBadgeComponent, CdkDropList, CdkDrag, CdkDragHandle],
  templateUrl: './round-list.component.html',
  styleUrl: './round-list.component.scss'
})
export class RoundListComponent {
  readonly rounds = input.required<RoundDraft[]>();
  readonly selectedClientId = input<number | null>(null);

  readonly select = output<number>();
  readonly moveUp = output<number>();
  readonly moveDown = output<number>();
  readonly remove = output<number>();
  readonly reorder = output<{ previousIndex: number; currentIndex: number }>();

  protected onDrop(event: CdkDragDrop<RoundDraft[]>): void {
    if (event.previousIndex === event.currentIndex) {
      return;
    }
    this.reorder.emit({ previousIndex: event.previousIndex, currentIndex: event.currentIndex });
  }
}
