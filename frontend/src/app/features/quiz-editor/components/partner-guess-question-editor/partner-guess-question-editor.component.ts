import { Component, effect, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';

interface PartnerGuessQuestionPayload {
  questionText: string;
  /** Reformulation affichée au(x) devineur(s) en phase 2. Vide = reprend questionText tel quel (voir
   * ResolveEffectivePayloadJsonAsync côté backend). */
  guesserQuestionText: string;
  // Toujours vide à l'édition : la "bonne réponse" est fournie en direct par le joueur désigné comme
  // répondant (voir GameSession.CurrentAnswererPlayerId côté backend), jamais pré-écrite ici.
  acceptedAnswers: string[];
}

function defaultPayload(): PartnerGuessQuestionPayload {
  return { questionText: '', guesserQuestionText: '', acceptedAnswers: [] };
}

@Component({
  selector: 'app-partner-guess-question-editor',
  imports: [FormsModule],
  templateUrl: './partner-guess-question-editor.component.html',
  styleUrl: './partner-guess-question-editor.component.scss'
})
export class PartnerGuessQuestionEditorComponent {
  readonly payloadJson = input.required<string>();
  readonly payloadJsonChange = output<string>();

  protected readonly payload = signal<PartnerGuessQuestionPayload>(defaultPayload());

  constructor() {
    effect(() => this.payload.set(this.parse(this.payloadJson())));
  }

  private parse(json: string): PartnerGuessQuestionPayload {
    try {
      return { ...defaultPayload(), ...JSON.parse(json), acceptedAnswers: [] };
    } catch {
      return defaultPayload();
    }
  }

  protected onQuestionTextChange(value: string): void {
    this.payload.update((p) => ({ ...p, questionText: value, acceptedAnswers: [] }));
    this.payloadJsonChange.emit(JSON.stringify(this.payload()));
  }

  protected onGuesserQuestionTextChange(value: string): void {
    this.payload.update((p) => ({ ...p, guesserQuestionText: value, acceptedAnswers: [] }));
    this.payloadJsonChange.emit(JSON.stringify(this.payload()));
  }
}
