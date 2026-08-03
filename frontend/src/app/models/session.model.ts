export type GameSessionStatus =
  | 'Lobby'
  | 'Running'
  | 'Paused'
  | 'RoundIntermission'
  | 'AwaitingParticipants'
  | 'ChoosingTheme'
  | 'Finished';

export interface Player {
  id: number;
  pseudo: string;
  /** Score perso uniquement. */
  score: number;
  teamId: number | null;
  /** Pot de l'équipe du joueur (0 si aucune équipe). */
  teamScore: number;
  /** score + teamScore — c'est celui-là qu'il faut afficher comme score "officiel" du joueur. */
  totalScore: number;
}

export interface Team {
  id: number;
  name: string;
  playerIds: number[];
  score: number;
}

export type ThemeResolution = 'Pending' | 'Played' | 'Skipped';

export interface ThemeBoardEntry {
  subRoundId: number;
  title: string;
  isRevealed: boolean;
  resolution: ThemeResolution;
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
  currentRoundParticipantPlayerIds: number[];
  currentRoundParticipantTeamIds: number[];
  teamScoringEnabled: boolean;
  currentBuzzHolderPlayerId: number | null;
  currentBuzzHolderPseudo: string | null;
  players: Player[];
  teams: Team[];
  /** Non-null seulement quand la manche courante est une manche à thèmes. */
  themeBoard: ThemeBoardEntry[] | null;
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
  secondsRemainingTotal: number;
  isAnswerWindowOpen: boolean;
  isBuzzerMode: boolean;
  correctFinders: string[];
  secondsElapsedTotal: number;
  isPaused: boolean;
}

export interface AnswerFeedItem {
  id: number;
  playerId: number;
  playerPseudo: string;
  rawAnswer: string;
  /** null = pas encore jugée (validation manuelle en attente). */
  isCorrect: boolean | null;
  pointsAwarded: number;
  pendingPoints: number;
  submittedAt: string;
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
  secondsRemainingTotal: number;
  isAnswerWindowOpen: boolean;
  hasAnswered: boolean;
  correctFinders: string[];
  isSpectator: boolean;
  isBuzzerMode: boolean;
  secondsElapsedTotal: number;
  isPaused: boolean;
}

export interface RoundPreview {
  roundTitle: string;
  featureTypeKey: string;
  /** Payload brut (non assaini) de la première question — vue GM uniquement, peut contenir les réponses acceptées. */
  firstQuestionPayloadJson: string | null;
}

export interface ZoomPublicPayload {
  imageUrl: string;
  zoomFocusX: number;
  zoomFocusY: number;
}

export interface QaPublicPayload {
  questionText: string;
}

export interface BlindTestPublicPayload {
  audioUrl: string;
}

export interface ImageGuessPublicPayload {
  imageUrl: string;
}
