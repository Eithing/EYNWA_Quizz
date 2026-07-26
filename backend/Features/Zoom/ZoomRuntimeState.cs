namespace QuizParty.Api.Features.Zoom;

public record ZoomRuntimeState(
    double CurrentLevel,
    int CurrentPoints,
    int SecondsRemainingInStep,
    bool IsAnswerWindowOpen,
    bool ShouldAutoAdvance);
