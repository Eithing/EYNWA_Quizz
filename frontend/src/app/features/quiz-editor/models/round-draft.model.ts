import { Question, Round } from '../../../models/quiz.model';

let nextClientId = 1;

export interface QuestionDraft extends Question {
  clientId: number;
}

export interface RoundDraft extends Omit<Round, 'questions'> {
  clientId: number;
  questions: QuestionDraft[];
}

export function toRoundDraft(round: Round): RoundDraft {
  return {
    ...round,
    clientId: nextClientId++,
    questions: round.questions.map(toQuestionDraft)
  };
}

export function toQuestionDraft(question: Question): QuestionDraft {
  return { ...question, clientId: nextClientId++ };
}

export function toRound(draft: RoundDraft): Round {
  const { clientId, questions, ...round } = draft;
  return { ...round, questions: questions.map(toQuestion) };
}

export function toQuestion(draft: QuestionDraft): Question {
  const { clientId, ...question } = draft;
  return question;
}
