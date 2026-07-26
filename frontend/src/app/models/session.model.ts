export type GameSessionStatus = 'Lobby' | 'Running' | 'Paused' | 'RoundIntermission' | 'AwaitingTargetPlayer' | 'Finished';

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
  currentRoundTargetPlayerId: number | null;
  currentRoundTargetPlayerPseudo: string | null;
  currentBuzzHolderPlayerId: number | null;
  currentBuzzHolderPseudo: string | null;
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
  isBuzzerMode: boolean;
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
  featureTypeKey: string;
  /** Assaini côté serveur selon la feature (ex: zoom-image -> {imageUrl, zoomFocusX, zoomFocusY} ; qa-text -> {questionText}) — jamais les réponses acceptées. */
  publicPayloadJson: string;
  currentLevel: number;
  secondsRemainingInStep: number;
  isAnswerWindowOpen: boolean;
  hasAnswered: boolean;
  correctFinders: string[];
  isSpectator: boolean;
  isBuzzerMode: boolean;
}

export interface ZoomPublicPayload {
  imageUrl: string;
  zoomFocusX: number;
  zoomFocusY: number;
}

export interface QaPublicPayload {
  questionText: string;
}
