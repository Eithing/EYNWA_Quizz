import { Question, Round } from '../../../models/quiz.model';

let nextClientId = 1;

export interface QuestionDraft extends Question {
  clientId: number;
}

export interface RoundDraft extends Omit<Round, 'questions' | 'subRounds'> {
  clientId: number;
  questions: QuestionDraft[];
  subRounds: RoundDraft[];
}

export function toRoundDraft(round: Round): RoundDraft {
  return {
    ...round,
    clientId: nextClientId++,
    questions: round.questions.map(toQuestionDraft),
    subRounds: (round.subRounds ?? []).map(toRoundDraft)
  };
}

export function toQuestionDraft(question: Question): QuestionDraft {
  return { ...question, clientId: nextClientId++ };
}

export function newRoundDraft(order: number): RoundDraft {
  return {
    clientId: nextClientId++,
    order,
    featureTypeKey: '',
    title: '',
    configJson: '{}',
    restrictsParticipants: false,
    isThemePicker: false,
    questions: [],
    subRounds: []
  };
}

export function toRound(draft: RoundDraft): Round {
  const { clientId, questions, subRounds, ...round } = draft;
  return { ...round, questions: questions.map(toQuestion), subRounds: subRounds.map(toRound) };
}

export function toQuestion(draft: QuestionDraft): Question {
  const { clientId, ...question } = draft;
  return question;
}
