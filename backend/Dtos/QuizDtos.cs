namespace QuizParty.Api.Dtos;

public record QuestionDto(int? Id, int Order, string PayloadJson);

public record RoundDto(int? Id, int Order, string FeatureTypeKey, string Title, string ConfigJson, List<QuestionDto> Questions);

public record QuizSummaryDto(int Id, string Title, string? Description, DateTime UpdatedAt, int RoundCount);

public record QuizDetailDto(int Id, string Title, string? Description, DateTime CreatedAt, DateTime UpdatedAt, List<RoundDto> Rounds);

public record SaveQuizRequest(string Title, string? Description, List<RoundDto> Rounds);
