using Server.Models;

namespace Server.Dtos;

public record QuizStepDto(int? Id, int OrderIndex, StepType Type, string Title, string ConfigJson);

public record QuizSummaryDto(int Id, string Title, string? Description, string InviteCode, DateTime UpdatedAtUtc, int StepCount);

public record QuizDetailDto(int Id, string Title, string? Description, string InviteCode, DateTime CreatedAtUtc, DateTime UpdatedAtUtc, List<QuizStepDto> Steps);

public record SaveQuizRequest(string Title, string? Description, List<QuizStepDto> Steps);
