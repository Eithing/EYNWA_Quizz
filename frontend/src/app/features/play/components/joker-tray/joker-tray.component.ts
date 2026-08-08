import { Component, input, output } from '@angular/core';
import { JOKER_DESCRIPTIONS, JOKER_ICONS, JOKER_LABELS, JokerGrant, JokerType, JokerUsedEvent } from '../../../../models/session.model';

@Component({
  selector: 'app-joker-tray',
  imports: [],
  templateUrl: './joker-tray.component.html',
  styleUrl: './joker-tray.component.scss'
})
export class JokerTrayComponent {
  readonly toast = input<JokerUsedEvent | null>(null);
  readonly jokers = input.required<JokerGrant[]>();
  readonly using = input(false);
  readonly error = input<string | null>(null);
  readonly jokerClicked = output<JokerType>();

  protected readonly jokerLabels = JOKER_LABELS;
  protected readonly jokerIcons = JOKER_ICONS;
  protected readonly jokerDescriptions = JOKER_DESCRIPTIONS;
}
