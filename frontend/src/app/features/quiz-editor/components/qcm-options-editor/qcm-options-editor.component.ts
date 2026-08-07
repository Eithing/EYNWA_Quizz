import { Component, computed, input, output } from '@angular/core';
import { FormsModule } from '@angular/forms';

export interface QcmOptionDraft {
  id: string;
  content: string;
  isCorrect: boolean;
  /** Uniquement pertinent si isCorrect. Null = utilise le barème uniforme de la manche. Ignoré si
   * pointsMode() !== 'PerAnswer'. */
  points: number | null;
}

/// Éditeur des options d'une question "Choix Multiple" : contenu texte + case "bonne réponse" + points
/// conditionnels (si correcte, en mode "points personnalisés"). Forme volontairement différente de
/// expected-answers-editor (pas de synonymes, une checkbox de correction à la place).
@Component({
  selector: 'app-qcm-options-editor',
  imports: [FormsModule],
  templateUrl: './qcm-options-editor.component.html',
  styleUrl: './qcm-options-editor.component.scss'
})
export class QcmOptionsEditorComponent {
  readonly options = input.required<QcmOptionDraft[]>();
  /** Mode déjà résolu par le parent (surcharge de la question, sinon réglage de la manche) — voir
   * qcm-question-editor.effectivePointsMode. */
  readonly pointsMode = input<'Uniform' | 'PerAnswer'>('Uniform');
  readonly optionsChange = output<QcmOptionDraft[]>();

  protected readonly correctCount = computed(() => this.options().filter((o) => o.isCorrect).length);

  protected onContentChange(index: number, value: string): void {
    const updated = [...this.options()];
    updated[index] = { ...updated[index], content: value };
    this.optionsChange.emit(updated);
  }

  protected onIsCorrectChange(index: number, value: boolean): void {
    const updated = [...this.options()];
    updated[index] = { ...updated[index], isCorrect: value, points: value ? updated[index].points : null };
    this.optionsChange.emit(updated);
  }

  protected onPointsChange(index: number, value: number): void {
    const updated = [...this.options()];
    updated[index] = { ...updated[index], points: value };
    this.optionsChange.emit(updated);
  }

  protected addOption(): void {
    this.optionsChange.emit([...this.options(), { id: crypto.randomUUID(), content: '', isCorrect: false, points: null }]);
  }

  protected removeOption(index: number): void {
    this.optionsChange.emit(this.options().filter((_, i) => i !== index));
  }
}
