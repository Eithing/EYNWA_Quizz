import { QuizStep } from '../../../models/quiz.model';

let nextClientId = 1;

export interface QuizStepDraft extends QuizStep {
  clientId: number;
}

export function toDraft(step: QuizStep): QuizStepDraft {
  return { ...step, clientId: nextClientId++ };
}

export function toQuizStep({ clientId, ...step }: QuizStepDraft): QuizStep {
  return step;
}
