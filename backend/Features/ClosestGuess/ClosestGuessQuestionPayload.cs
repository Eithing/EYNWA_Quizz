namespace QuizParty.Api.Features.ClosestGuess;

/// <summary>Désérialisé depuis Question.PayloadJson pour une manche "closest-guess".</summary>
public class ClosestGuessQuestionPayload
{
    public string QuestionText { get; set; } = "";
    public double TargetValue { get; set; }
}
