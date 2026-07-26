namespace QuizParty.Api.Dtos;

/// <summary>Vue joueur d'une question zoom-image : jamais les réponses acceptées.</summary>
public record PlayerQuestionDto(
    int QuestionId,
    string RoundTitle,
    string ImageUrl,
    double ZoomFocusX,
    double ZoomFocusY,
    double CurrentLevel,
    int SecondsRemainingInStep,
    bool IsAnswerWindowOpen,
    bool HasAnswered,
    List<string> CorrectFinders);
