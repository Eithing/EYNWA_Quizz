import { Component, computed, input, output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ExpectedAnswerDraft, ExpectedAnswersEditorComponent } from '../expected-answers-editor/expected-answers-editor.component';
import { PointsMode, roundPointsModeFrom, syncPayloadFromJson, toExpectedAnswers } from '../question-editor-payload.util';

interface QaQuestionPayload {
  questionText: string;
  /** Legacy, lu uniquement pour la rétrocompatibilité — voir toExpectedAnswers(). */
  acceptedAnswers: string[];
  expectedAnswers: ExpectedAnswerDraft[];
  /** Surcharge du mode de points de la manche pour CETTE question. Null = suit le réglage de la manche. */
  pointsModeOverride: 'Uniform' | 'PerAnswer' | null;
}

function defaultPayload(): QaQuestionPayload {
  return { questionText: '', acceptedAnswers: [], expectedAnswers: [], pointsModeOverride: null };
}

@Component({
  selector: 'app-qa-question-editor',
  imports: [FormsModule, ExpectedAnswersEditorComponent],
  templateUrl: './qa-question-editor.component.html',
  styleUrl: './qa-question-editor.component.scss'
})
export class QaQuestionEditorComponent {
  readonly payloadJson = input.required<string>();
  readonly configJson = input<string>('{}');
  readonly payloadJsonChange = output<string>();

  // Migration douce à l'affichage : dès que l'éditeur ré-émet (n'importe quelle modification), le
  // payload repart au nouveau format — acceptedAnswers ne sera alors plus jamais réécrit.
  protected readonly payload = syncPayloadFromJson(this.payloadJson, defaultPayload, (parsed) => ({
    ...parsed,
    expectedAnswers: toExpectedAnswers(parsed)
  }));

  /** Réglage par défaut de la manche (round-config), avant surcharge éventuelle par cette question. */
  protected readonly roundPointsMode = computed<PointsMode>(() => roundPointsModeFrom(this.configJson()));

  /** Mode réellement appliqué à cette question : sa propre surcharge si renseignée, sinon celui de la manche. */
  protected readonly effectivePointsMode = computed<PointsMode>(() => this.payload().pointsModeOverride ?? this.roundPointsMode());

  private emit(): void {
    this.payloadJsonChange.emit(JSON.stringify(this.payload()));
  }

  protected onQuestionTextChange(value: string): void {
    this.payload.update((p) => ({ ...p, questionText: value }));
    this.emit();
  }

  protected onExpectedAnswersChange(expectedAnswers: ExpectedAnswerDraft[]): void {
    // acceptedAnswers legacy vidé : ce payload est désormais toujours lu via expectedAnswers.
    this.payload.update((p) => ({ ...p, expectedAnswers, acceptedAnswers: [] }));
    this.emit();
  }

  protected onPointsModeOverrideChange(value: string): void {
    this.payload.update((p) => ({ ...p, pointsModeOverride: value === '' ? null : (value as 'Uniform' | 'PerAnswer') }));
    this.emit();
  }
}
