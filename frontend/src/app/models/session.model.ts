export type GameSessionStatus = 'Lobby' | 'Running' | 'Paused' | 'RoundIntermission' | 'Finished';

export interface Player {
  id: number;
  pseudo: string;
  score: number;
}

export interface GameSessionState {
  sessionId: number;
  inviteToken: string;
  quizTitle: string;
  status: GameSessionStatus;
  currentRoundIndex: number;
  currentQuestionIndex: number;
  roundCount: number;
  scoreboardVisible: boolean;
  players: Player[];
}

export interface CurrentQuestionAdmin {
  roundId: number;
  roundTitle: string;
  featureTypeKey: string;
  questionId: number;
  payloadJson: string;
  configJson: string;
  currentLevel: number;
  currentPoints: number;
  secondsRemainingInStep: number;
  isAnswerWindowOpen: boolean;
  correctFinders: string[];
}

export interface PendingAnswer {
  id: number;
  playerId: number;
  playerPseudo: string;
  rawAnswer: string;
  submittedAt: string;
  pendingPoints: number;
}

export interface JoinSessionResponse {
  playerId: number;
  connectionToken: string;
  sessionId: number;
}

export interface SubmitAnswerResponse {
  isCorrect: boolean | null;
  pointsAwarded: number;
  validationMode: 'Auto' | 'Manual';
}

export interface PlayerQuestion {
  questionId: number;
  roundTitle: string;
  imageUrl: string;
  zoomFocusX: number;
  zoomFocusY: number;
  currentLevel: number;
  secondsRemainingInStep: number;
  isAnswerWindowOpen: boolean;
  hasAnswered: boolean;
  correctFinders: string[];
}
