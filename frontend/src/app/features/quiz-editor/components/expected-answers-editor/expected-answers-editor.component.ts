import { Component, input, output } from '@angular/core';
import { FormsModule } from '@angular/forms';

export interface ExpectedAnswerDraft {
  acceptedVariants: string[];
  /** Null = utilise le barème uniforme de la manche. Ignoré si pointsMode() !== 'PerAnswer'. */
  points: number | null;
}

/// Composant partagé par les 4 éditeurs de question (qa-text, zoom-image, blind-test, image-guess) : une
/// question peut désormais attendre plusieurs réponses DISTINCTES (pas de simples synonymes d'une seule
/// réponse), chacune avec ses propres synonymes tolérés et, en mode "points personnalisés", son propre
/// barème. L'ordre des réponses n'a aucune incidence sur le matching côté joueur, pas de glisser-déposer ici.
@Component({
  selector: 'app-expected-answers-editor',
  imports: [FormsModule],
  templateUrl: './expected-answers-editor.component.html',
  styleUrl: './expected-answers-editor.component.scss'
})
export class ExpectedAnswersEditorComponent {
  readonly expectedAnswers = input.required<ExpectedAnswerDraft[]>();
  readonly pointsMode = input<'Uniform' | 'PerAnswer'>('Uniform');
  readonly expectedAnswersChange = output<ExpectedAnswerDraft[]>();

  protected variantsText(index: number): string {
    return this.expectedAnswers()[index]?.acceptedVariants.join(', ') ?? '';
  }

  protected onVariantsChange(index: number, value: string): void {
    const acceptedVariants = value
      .split(',')
      .map((v) => v.trim())
      .filter((v) => v.length > 0);

    const updated = [...this.expectedAnswers()];
    updated[index] = { ...updated[index], acceptedVariants };
    this.expectedAnswersChange.emit(updated);
  }

  protected onPointsChange(index: number, value: number): void {
    const updated = [...this.expectedAnswers()];
    updated[index] = { ...updated[index], points: value };
    this.expectedAnswersChange.emit(updated);
  }

  protected addAnswer(): void {
    this.expectedAnswersChange.emit([...this.expectedAnswers(), { acceptedVariants: [], points: null }]);
  }

  protected removeAnswer(index: number): void {
    this.expectedAnswersChange.emit(this.expectedAnswers().filter((_, i) => i !== index));
  }
}
