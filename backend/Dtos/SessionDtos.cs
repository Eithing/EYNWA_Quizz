using QuizParty.Api.Models;

namespace QuizParty.Api.Dtos;

/// <summary>Score = perso uniquement ; TeamScore = pot de l'équipe du joueur (0 si aucune équipe) ;
/// TotalScore = les deux additionnés, c'est lui qui doit servir au classement affiché.</summary>
public record PlayerDto(int Id, string Pseudo, int Score, int? TeamId, int TeamScore, int TotalScore);

public record TeamDto(int Id, string Name, List<int> PlayerIds, int Score);

/// <summary>Une case du plateau d'une manche à thèmes. Resolution : "Pending" | "Played" | "Skipped".</summary>
public record ThemeBoardEntryDto(int SubRoundId, string Title, bool IsRevealed, string Resolution);

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
    List<ThemeBoardEntryDto>? ThemeBoard);

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
    bool IsPaused);

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

/// <summary>Aperçu GM d'une manche pas encore démarrée (ex: avant de désigner les participants), pour savoir sur quoi elle porte.</summary>
public record RoundPreviewDto(string RoundTitle, string FeatureTypeKey, string? FirstQuestionPayloadJson);
