namespace QuizParty.Api.Features.BlindTest;

/// <summary>Désérialisé depuis Question.PayloadJson pour une manche "blind-test".</summary>
public class BlindTestQuestionPayload
{
    public string AudioUrl { get; set; } = "";
    public List<string> AcceptedAnswers { get; set; } = [];
}
