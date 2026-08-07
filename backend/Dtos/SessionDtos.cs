using QuizParty.Api.Models;

namespace QuizParty.Api.Dtos;

/// <summary>Score = perso uniquement ; TeamScore = pot de l'équipe du joueur (0 si aucune équipe) ;
/// TotalScore = les deux additionnés, c'est lui qui doit servir au classement affiché.</summary>
public record PlayerDto(int Id, string Pseudo, int Score, int? TeamId, int TeamScore, int TotalScore);

public record TeamDto(int Id, string Name, List<int> PlayerIds, int Score);

/// <summary>Une case du plateau d'une manche à thèmes. Resolution : "Pending" | "Played" | "Skipped".</summary>
public record ThemeBoardEntryDto(int SubRoundId, string Title, bool IsRevealed, string Resolution);

/// <summary>order-list, vue GM uniquement : l'état courant d'un groupe (un joueur solo, ou toute une
/// équipe) pour la question en cours — plusieurs groupes jouent en parallèle, chacun avec son propre
/// ordre en train d'être réarrangé.</summary>
public record OrderListGroupStateDto(string GroupLabel, List<string> CurrentOrder, bool IsResolved, int? PointsAwarded);

/// <summary>Devinette résolue d'un joueur pour un tirage aléatoire — Rank en classement "olympique" (des
/// égalités partagent le même rang, le suivant saute d'autant), IsWinner vrai pour tout le rang 0.</summary>
public record RandomDrawResultEntryDto(int PlayerId, string PlayerPseudo, int GuessValue, int Rank, bool IsWinner);

/// <summary>Outil host "tirage aléatoire" actif pour la session (un seul à la fois, indépendant de
/// GameSessionStatus) — Results reste null tant que non résolu (Reveal : résolu immédiatement à la
/// création ; Guess* : résolu via /random-draw/reveal). SubmittedPlayerIds : qui a déjà deviné, jamais ce
/// qu'il a deviné avant résolution (ce DTO est partagé par tous les viewers, pas de fetch par joueur).</summary>
public record RandomDrawStateDto(
    int Id,
    string Mode,
    string Label,
    int MinValue,
    int MaxValue,
    /// <summary>Vide = tout le monde concerné.</summary>
    List<int> ConcernedPlayerIds,
    List<int> SubmittedPlayerIds,
    bool IsResolved,
    int? DrawnValue,
    List<RandomDrawResultEntryDto>? Results);

public record StrawPollOptionDto(string Id, string Text);

public record StrawPollResultDto(string OptionId, int VoteCount);

/// <summary>Outil host "sondage" actif pour la session — même principe de gating que ScoreboardVisible :
/// Results reste null tant que ResultsRevealed est faux, même côté host (DTO partagé, pas de bypass).</summary>
public record StrawPollStateDto(
    int Id,
    string Question,
    List<StrawPollOptionDto> Options,
    bool AllowMultipleVotes,
    /// <summary>Vide = tout le monde concerné.</summary>
    List<int> ConcernedPlayerIds,
    List<int> VotedPlayerIds,
    bool ResultsRevealed,
    List<StrawPollResultDto>? Results);

/// <summary>Stock de charges d'un joker attribué à un joueur OU une équipe (jamais les deux) — Type :
/// "Exchange" | "AloneInTheWorld" | "CopyPaste" | "MeFirst" | "FiftyFifty". Diffusé pour toute la
/// session (petite volumétrie) ; chaque client filtre localement ce qui le concerne.</summary>
public record JokerGrantDto(int Id, string Type, int? OwnerPlayerId, int? OwnerTeamId, int Charges);

public record JokerGrantInput(string Type, int? PlayerId, int? TeamId, int Charges);

/// <summary>Remplace l'inventaire complet des jokers de la session — appelé depuis le lobby, peut être
/// rappelé pour ajuster tant que la partie n'a pas démarré.</summary>
public record SetJokerGrantsRequest(List<JokerGrantInput> Grants);

public record UseJokerRequest(Guid ConnectionToken, string Type, int? TargetPlayerId);

/// <summary>Toast temps réel diffusé à l'utilisation d'un joker (événement SignalR "JokerUsed", pas dans
/// GameSessionStateDto — un pop-up ponctuel, pas un état à re-synchroniser).</summary>
public record JokerUsedEventDto(string Type, string ActorLabel, string? TargetLabel, string? Detail);

public record GameSessionStateDto(
    int SessionId,
    string InviteToken,
    string QuizTitle,
    GameSessionStatus Status,
    int CurrentRoundIndex,
    int CurrentQuestionIndex,
    int RoundCount,
    bool ScoreboardVisible,
    List<int> CurrentRoundParticipantPlayerIds,
    List<int> CurrentRoundParticipantTeamIds,
    bool TeamScoringEnabled,
    int? CurrentBuzzHolderPlayerId,
    string? CurrentBuzzHolderPseudo,
    List<PlayerDto> Players,
    List<TeamDto> Teams,
    List<ThemeBoardEntryDto>? ThemeBoard,
    /// <summary>Manche "à quoi pense l'autre" : joueur désigné pour répondre en privé à la question courante.</summary>
    int? CurrentAnswererPlayerId,
    string? CurrentAnswererPseudo,
    /// <summary>Outil host actif, indépendant de Status — null si aucun tirage en cours.</summary>
    RandomDrawStateDto? ActiveRandomDraw = null,
    /// <summary>Outil host actif, indépendant de Status — null si aucun sondage en cours.</summary>
    StrawPollStateDto? ActiveStrawPoll = null,
    /// <summary>Inventaire complet des jokers de la session — chaque client filtre localement ce qui le concerne.</summary>
    List<JokerGrantDto>? JokerGrants = null,
    /// <summary>Joker Seul au monde : détenteur sur la question courante, remis à null à chaque nouvelle question.</summary>
    int? AloneInTheWorldPlayerId = null,
    int? AloneInTheWorldTeamId = null,
    /// <summary>Joker Moi d'abord : détenteur du verrou buzzer courant.</summary>
    int? MeFirstHolderPlayerId = null,
    int? MeFirstHolderTeamId = null,
    int MeFirstQuestionsRemaining = 0,
    /// <summary>Vrai dès que le détenteur a buzzé sur la question courante — le verrou ne bloque alors
    /// plus les autres joueurs pour le reste de cette question (retry classique).</summary>
    bool MeFirstConsumedThisQuestion = false);

public record CurrentQuestionAdminDto(
    int RoundId,
    string RoundTitle,
    string FeatureTypeKey,
    int QuestionId,
    string PayloadJson,
    string ConfigJson,
    double CurrentLevel,
    int CurrentPoints,
    int SecondsRemainingInStep,
    int SecondsRemainingTotal,
    bool IsAnswerWindowOpen,
    bool IsBuzzerMode,
    List<string> CorrectFinders,
    double SecondsElapsedTotal,
    bool IsPaused,
    /// <summary>Vrai pour une feature à résolution différée (closest-guess) quand il reste des réponses en
    /// attente de classement — le host peut alors déclencher la révélation manuellement.</summary>
    bool AwaitingDeferredResolution,
    /// <summary>order-list uniquement : l'état de chaque groupe (joueur solo ou équipe) en train de jouer
    /// cette question. Null pour toute autre feature.</summary>
    List<OrderListGroupStateDto>? OrderListGroups = null);

/// <summary>Vue GM de toutes les réponses reçues pour la question courante, jugées ou non — permet de voir
/// passer les réponses même en validation automatique et de corriger un verdict à tout moment.</summary>
public record AnswerFeedDto(
    int Id,
    int PlayerId,
    string PlayerPseudo,
    string RawAnswer,
    bool? IsCorrect,
    int PointsAwarded,
    int PendingPoints,
    DateTime SubmittedAt);

public record JoinSessionRequest(string Pseudo);

public record JoinSessionResponse(int PlayerId, Guid ConnectionToken, int SessionId);

public record SubmitAnswerRequest(Guid ConnectionToken, string RawAnswer);

public record BuzzRequest(Guid ConnectionToken);

public record SubmitAnswerResponse(bool? IsCorrect, int PointsAwarded, string ValidationMode);

/// <summary>order-list : nouvel ordre après un glisser-déposer terminé — met à jour le brouillon du
/// groupe (joueur seul, ou toute son équipe en mode équipe), ne note rien.</summary>
public record OrderDraftRequest(Guid ConnectionToken, List<string> ItemOrder);

public record OrderSubmitRequest(Guid ConnectionToken);

public record OrderSubmitResponse(int PointsAwarded, List<string> ChainItemIds);

public record ValidateAnswerRequest(bool IsCorrect);

/// <summary>Ajustement du score perso d'un joueur (PlayerId requis) — voir aussi TeamScoreAdjustmentRequest pour le pot d'équipe.</summary>
public record ScoreAdjustmentRequest(int? PlayerId, int? QuestionId, int Delta, string Reason);

public record TeamScoreAdjustmentRequest(int Delta, string Reason);

public record SetScoreboardVisibleRequest(bool Visible);

/// <summary>Sélection de participants pour la manche restreinte en attente : soit des joueurs, soit des
/// équipes (jamais les deux à la fois) ; au moins une des deux listes non vide.</summary>
public record SetRoundParticipantsRequest(List<int> PlayerIds, List<int> TeamIds);

public record CreateTeamDto(string Name, List<int> PlayerIds);

public record SetTeamsRequest(List<CreateTeamDto> Teams);

public record SetTeamScoringRequest(bool Enabled);

public record ResolveBuzzRequest(bool IsCorrect);

/// <summary>Choix d'un thème (sous-manche) par le GM, avec la sélection de participants qui va avec —
/// une seule action côté host, pas de détour par un état d'attente séparé.</summary>
public record ChooseThemeRequest(List<int> PlayerIds, List<int> TeamIds);

/// <summary>SubRoundId null = révèle tous les thèmes du plateau d'un coup.</summary>
public record RevealThemeRequest(int? SubRoundId);

public record SetPartnerGuessAnswererRequest(int PlayerId);

/// <summary>Aperçu GM d'une manche pas encore démarrée (ex: avant de désigner les participants), pour savoir sur quoi elle porte.</summary>
public record RoundPreviewDto(string RoundTitle, string FeatureTypeKey, string? FirstQuestionPayloadJson);

/// <summary>Mode : "Reveal" | "GuessWinner" | "GuessRanking". PlayerIds/TeamIds vides = tout le monde
/// concerné (contrairement à SetRoundParticipantsRequest, vide est ici une valeur valide).</summary>
public record StartRandomDrawRequest(string Mode, string Label, int MinValue, int MaxValue, List<int> PlayerIds, List<int> TeamIds);

public record RandomDrawGuessRequest(Guid ConnectionToken, int GuessValue);

/// <summary>PlayerIds/TeamIds vides = tout le monde concerné.</summary>
public record StartStrawPollRequest(string Question, List<string> Options, bool AllowMultipleVotes, List<int> PlayerIds, List<int> TeamIds);

public record StrawPollVoteRequest(Guid ConnectionToken, List<string> OptionIds);
