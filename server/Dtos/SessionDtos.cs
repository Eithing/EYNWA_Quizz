using Server.Models;

namespace Server.Dtos;

public record PlayerDto(int Id, string Name, int Score);

public record SessionStateDto(
    int SessionId,
    string InviteCode,
    string QuizTitle,
    SessionStatus Status,
    int CurrentStepIndex,
    int StepCount,
    List<PlayerDto> Players);

public record JoinSessionRequest(string Name);

public record JoinSessionResponse(int PlayerId, Guid ClientToken, int SessionId);

public record PlayerStepDto(int StepId, int OrderIndex, StepType Type, string Title, string ConfigJson, bool HasAnswered);

public record SubmitAnswerRequest(Guid ClientToken, string Answer);

public record SubmitAnswerResponse(bool IsCorrect, int PointsAwarded, int TotalScore);
