import { Component, effect, input, output, signal } from '@angular/core';
import { RandomDrawState } from '../../../../models/session.model';
import { UiCardComponent } from '../../../../shared/components/ui-card/ui-card.component';

@Component({
  selector: 'app-random-draw-overlay',
  imports: [UiCardComponent],
  templateUrl: './random-draw-overlay.component.html',
  styleUrl: './random-draw-overlay.component.scss'
})
export class RandomDrawOverlayComponent {
  readonly draw = input.required<RandomDrawState>();
  readonly hasSubmitted = input.required<boolean>();
  readonly submitting = input(false);
  readonly error = input<string | null>(null);
  readonly guessSubmitted = output<number>();

  protected readonly guessValue = signal(0);

  constructor() {
    // Vide le formulaire dès qu'un nouveau tirage démarre (ID différent).
    let lastDrawId: number | null = null;
    effect(() => {
      const draw = this.draw();
      if (draw.id !== lastDrawId) {
        lastDrawId = draw.id;
        this.guessValue.set(draw.minValue);
      }
    });
  }

  protected submit(): void {
    this.guessSubmitted.emit(this.guessValue());
  }
}
