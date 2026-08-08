export type GameSessionStatus =
  | 'Lobby'
  | 'Running'
  | 'Paused'
  | 'RoundIntermission'
  | 'AwaitingParticipants'
  | 'ChoosingTheme'
  | 'AwaitingAnswerer'
  | 'AwaitingTeamMode'
  | 'ThemeReadyToLaunch'
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

export type JokerType = 'Exchange' | 'AloneInTheWorld' | 'CopyPaste' | 'MeFirst' | 'FiftyFifty';

export const JOKER_TYPES: JokerType[] = ['Exchange', 'AloneInTheWorld', 'CopyPaste', 'MeFirst', 'FiftyFifty'];

export const JOKER_LABELS: Record<JokerType, string> = {
  Exchange: 'Échange',
  AloneInTheWorld: 'Seul au monde',
  CopyPaste: 'Copier/coller',
  MeFirst: "Moi d'abord",
  FiftyFifty: 'Cinquante-cinquante'
};

export const JOKER_ICONS: Record<JokerType, string> = {
  Exchange: '🔄',
  AloneInTheWorld: '🏝️',
  CopyPaste: '📋',
  MeFirst: '🏎️',
  FiftyFifty: '5️⃣0️⃣'
};

/** Phrase courte pour l'infobulle au survol — rappel du joker en cours de partie, même si l'hôte est
 * censé l'expliquer en début de partie. */
export const JOKER_DESCRIPTIONS: Record<JokerType, string> = {
  Exchange: "Vole la désignation d'un thème pas encore lancé.",
  AloneInTheWorld: 'Sur cette question, seule ta réponse rapporte des points — les autres joueurs jouent pour rien.',
  CopyPaste: "Copie la réponse d'un autre joueur, révélée seulement à la fin de la question.",
  MeFirst: 'Priorité sur le buzzer pendant les 2 prochaines questions.',
  FiftyFifty: 'Retire la moitié des mauvaises options du QCM, pour toi uniquement.'
};

/** Stock de charges d'un joker attribué à un joueur OU une équipe (jamais les deux). */
export interface JokerGrant {
  id: number;
  type: JokerType;
  ownerPlayerId: number | null;
  ownerTeamId: number | null;
  charges: number;
}

/** Toast temps réel diffusé à l'utilisation d'un joker (événement SignalR "JokerUsed"). */
export interface JokerUsedEvent {
  type: JokerType;
  actorLabel: string;
  targetLabel: string | null;
  detail: string | null;
}

export type RandomDrawMode = 'Reveal' | 'GuessWinner' | 'GuessRanking';

/** Classement "olympique" par proximité : Rank partagé entre égalités, IsWinner vrai pour tout le rang 0. */
export interface RandomDrawResultEntry {
  playerId: number;
  playerPseudo: string;
  guessValue: number;
  rank: number;
  isWinner: boolean;
}

/** Outil host actif, indépendant de GameSessionStatus. results reste null tant que non résolu. */
export interface RandomDrawState {
  id: number;
  mode: RandomDrawMode;
  label: string;
  minValue: number;
  maxValue: number;
  /** Vide = tout le monde concerné. */
  concernedPlayerIds: number[];
  submittedPlayerIds: number[];
  isResolved: boolean;
  drawnValue: number | null;
  results: RandomDrawResultEntry[] | null;
}

export interface StrawPollOption {
  id: string;
  text: string;
}

export interface StrawPollResult {
  optionId: string;
  voteCount: number;
}

/** Outil host actif, indépendant de GameSessionStatus. results reste null tant que resultsRevealed est faux. */
export interface StrawPollState {
  id: number;
  question: string;
  options: StrawPollOption[];
  allowMultipleVotes: boolean;
  /** Vide = tout le monde concerné. */
  concernedPlayerIds: number[];
  votedPlayerIds: number[];
  resultsRevealed: boolean;
  results: StrawPollResult[] | null;
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
  /** Manche "à quoi pense l'autre" : joueur désigné pour répondre en privé à la question courante. */
  currentAnswererPlayerId: number | null;
  currentAnswererPseudo: string | null;
  /** Outil host actif, indépendant de status — null si aucun tirage en cours. */
  activeRandomDraw: RandomDrawState | null;
  /** Outil host actif, indépendant de status — null si aucun sondage en cours. */
  activeStrawPoll: StrawPollState | null;
  /** Inventaire complet des jokers de la session — chaque client filtre localement ce qui le concerne. */
  jokerGrants: JokerGrant[];
  /** Joker Seul au monde : détenteur sur la question courante, null si aucun. */
  aloneInTheWorldPlayerId: number | null;
  aloneInTheWorldTeamId: number | null;
  /** Joker Moi d'abord : détenteur du verrou buzzer courant. */
  meFirstHolderPlayerId: number | null;
  meFirstHolderTeamId: number | null;
  meFirstQuestionsRemaining: number;
  /** Vrai dès que le détenteur a buzzé sur la question courante — le verrou ne bloque alors plus les
   * autres joueurs pour le reste de cette question (retry classique). */
  meFirstConsumedThisQuestion: boolean;
  /** Sous-manche (thème) désignée par ChooseTheme, en attente de LaunchTheme. */
  currentThemeSubRoundId: number | null;
  /** Vrai si la dernière action annulable (Next / ChooseTheme / LaunchTheme / SkipTheme) peut être
   * défaite via /undo. */
  hasUndoSnapshot: boolean;
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
  /** Vrai pour closest-guess quand il reste des estimations en attente de classement. */
  awaitingDeferredResolution: boolean;
  /** order-list uniquement : l'état de chaque groupe (joueur solo ou équipe) en train de jouer cette question. */
  orderListGroups: OrderListGroupState[] | null;
}

export interface OrderListGroupState {
  groupLabel: string;
  currentOrder: string[];
  isResolved: boolean;
  pointsAwarded: number | null;
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

export interface OrderSubmitResponse {
  pointsAwarded: number;
  chainItemIds: string[];
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
  /** Résultat de la dernière tentative du joueur, une fois jugée — null tant qu'aucun verdict n'existe
   * (utile pour les features à résolution différée comme closest-guess). */
  myLastAnswerIsCorrect: boolean | null;
  myLastAnswerPoints: number | null;
  /** closest-guess uniquement : tous les essais, visibles dès la fenêtre fermée (avant même la
   * révélation du classement — isCorrect/pointsAwarded par entrée restent null jusque-là). */
  closestGuessEntries: ClosestGuessEntry[] | null;
  /** closest-guess uniquement : la vraie valeur, révélée seulement une fois le classement résolu. */
  closestGuessTargetValue: number | null;
  /** order-list uniquement : ordre courant des IDs d'items du groupe du joueur (lui seul, ou toute son
   * équipe si le mode équipe est actif) — mis à jour en quasi temps réel à chaque glisser-déposer. */
  orderListCurrentOrder: string[] | null;
  /** order-list uniquement : l'ordre correct, révélé seulement une fois le brouillon finalisé. */
  orderListCorrectOrder: string[] | null;
  /** order-list uniquement : IDs des items qui appartenaient à la plus longue chaîne bien enchaînée. */
  orderListChainItemIds: string[] | null;
  /** order-list uniquement : points obtenus par le groupe sur cette question, une fois finalisé. */
  orderListPointsAwarded: number | null;
}

export interface ClosestGuessEntry {
  playerPseudo: string;
  rawAnswer: string;
  isCorrect: boolean | null;
  pointsAwarded: number | null;
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
  expectedAnswerCount: number;
  /** Null en mode "points uniformes" (les points dépendent du palier de zoom courant, déjà affiché
   * ailleurs) — non-null seulement en mode "points personnalisés par réponse". */
  expectedAnswerPoints: number[] | null;
  comment: string;
}

export interface QaPublicPayload {
  questionText: string;
  expectedAnswerCount: number;
  expectedAnswerPoints: number[] | null;
}

export interface BlindTestPublicPayload {
  audioUrl: string;
  expectedAnswerCount: number;
  expectedAnswerPoints: number[] | null;
  comment: string;
}

export interface ImageGuessPublicPayload {
  imageUrl: string;
  expectedAnswerCount: number;
  expectedAnswerPoints: number[] | null;
  comment: string;
}

export interface ClosestGuessPublicPayload {
  questionText: string;
}

export interface OrderListItem {
  id: string;
  content: string;
}

export interface OrderListPublicPayload {
  questionText: string;
  contentType: 'Text' | 'Image' | 'Audio';
  items: OrderListItem[];
}

export interface QcmOptionPublic {
  id: string;
  content: string;
}

export interface QcmPublicPayload {
  questionText: string;
  options: QcmOptionPublic[];
  /** Nombre maximum d'options sélectionnables (= nombre de bonnes réponses) — plafond anti-triche. */
  maxSelectable: number;
  /** Valeurs de points des bonnes réponses, sans association à une option précise. */
  correctOptionPoints: number[];
}
