namespace QuizParty.Api.Features.Qa;

/// <summary>Désérialisé depuis Question.PayloadJson pour une manche "qa-text".</summary>
public class QaQuestionPayload
{
    public string QuestionText { get; set; } = "";
    public List<string> AcceptedAnswers { get; set; } = [];
}
