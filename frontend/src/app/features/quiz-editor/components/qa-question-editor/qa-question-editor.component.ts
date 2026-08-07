import { Component, computed, effect, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ExpectedAnswerDraft, ExpectedAnswersEditorComponent } from '../expected-answers-editor/expected-answers-editor.component';

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

/** ExpectedAnswers si renseigné, sinon reconstruit depuis l'ancien acceptedAnswers plat (une seule
 * réponse attendue, synonymes = l'ancienne liste) — miroir de QaQuestionPayload.ExpectedAnswersOrLegacy()
 * côté backend, pour que l'éditeur affiche correctement les questions créées avant les réponses multiples. */
function toExpectedAnswers(payload: QaQuestionPayload): ExpectedAnswerDraft[] {
  if (payload.expectedAnswers.length > 0) {
    return payload.expectedAnswers;
  }
  if (payload.acceptedAnswers.length > 0) {
    return [{ acceptedVariants: payload.acceptedAnswers, points: null }];
  }
  return [];
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

  protected readonly payload = signal<QaQuestionPayload>(defaultPayload());

  /** Réglage par défaut de la manche (round-config), avant surcharge éventuelle par cette question. */
  protected readonly roundPointsMode = computed<'Uniform' | 'PerAnswer'>(() => {
    try {
      const parsed = JSON.parse(this.configJson());
      return parsed.pointsMode === 'PerAnswer' ? 'PerAnswer' : 'Uniform';
    } catch {
      return 'Uniform';
    }
  });

  /** Mode réellement appliqué à cette question : sa propre surcharge si renseignée, sinon celui de la manche. */
  protected readonly effectivePointsMode = computed<'Uniform' | 'PerAnswer'>(
    () => this.payload().pointsModeOverride ?? this.roundPointsMode()
  );

  constructor() {
    effect(() => {
      const parsed = this.parse(this.payloadJson());
      // Migration douce à l'affichage : dès que l'éditeur ré-émet (n'importe quelle modification), le
      // payload repart au nouveau format — acceptedAnswers ne sera alors plus jamais réécrit.
      this.payload.set({ ...parsed, expectedAnswers: toExpectedAnswers(parsed) });
    });
  }

  private parse(json: string): QaQuestionPayload {
    try {
      return { ...defaultPayload(), ...JSON.parse(json) };
    } catch {
      return defaultPayload();
    }
  }

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
