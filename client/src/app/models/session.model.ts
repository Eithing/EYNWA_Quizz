import { StepType } from './quiz-config.model';

export type SessionStatus = 'Lobby' | 'InProgress' | 'Finished';

export interface Player {
  id: number;
  name: string;
  score: number;
}

export interface SessionState {
  sessionId: number;
  inviteCode: string;
  quizTitle: string;
  status: SessionStatus;
  currentStepIndex: number;
  stepCount: number;
  players: Player[];
}

export interface JoinSessionResponse {
  playerId: number;
  clientToken: string;
  sessionId: number;
}

export interface PlayerStep {
  stepId: number;
  orderIndex: number;
  type: StepType;
  title: string;
  configJson: string;
  hasAnswered: boolean;
}

export interface SubmitAnswerResponse {
  isCorrect: boolean;
  pointsAwarded: number;
  totalScore: number;
}
