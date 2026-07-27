using QuizParty.Api.Models;

namespace QuizParty.Api.Dtos;

public record PlayerDto(int Id, string Pseudo, int Score);

public record GameSessionStateDto(
    int SessionId,
    string InviteToken,
    string QuizTitle,
    GameSessionStatus Status,
    int CurrentRoundIndex,
    int CurrentQuestionIndex,
    int RoundCount,
    bool ScoreboardVisible,
    int? CurrentRoundTargetPlayerId,
    string? CurrentRoundTargetPlayerPseudo,
    int? CurrentBuzzHolderPlayerId,
    string? CurrentBuzzHolderPseudo,
    List<PlayerDto> Players);

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
    List<string> CorrectFinders);

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

public record ScoreAdjustmentRequest(int PlayerId, int? QuestionId, int Delta, string Reason);

public record SetScoreboardVisibleRequest(bool Visible);

public record SetRoundTargetPlayerRequest(int PlayerId);

public record ResolveBuzzRequest(bool IsCorrect);

/// <summary>Aperçu GM d'une manche pas encore démarrée (ex: avant de désigner le joueur ciblé), pour savoir sur quoi elle porte.</summary>
public record RoundPreviewDto(string RoundTitle, string FeatureTypeKey, string? FirstQuestionPayloadJson);
