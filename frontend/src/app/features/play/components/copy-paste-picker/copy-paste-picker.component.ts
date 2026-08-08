import { Component, input, output } from '@angular/core';
import { Player } from '../../../../models/session.model';
import { UiCardComponent } from '../../../../shared/components/ui-card/ui-card.component';

@Component({
  selector: 'app-copy-paste-picker',
  imports: [UiCardComponent],
  templateUrl: './copy-paste-picker.component.html',
  styleUrl: './copy-paste-picker.component.scss'
})
export class CopyPastePickerComponent {
  readonly targets = input.required<Player[]>();
  readonly using = input(false);
  readonly error = input<string | null>(null);
  readonly pick = output<number>();
  readonly cancel = output<void>();
}
