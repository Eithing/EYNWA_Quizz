namespace QuizParty.Api.Features.ImageGuess;

/// <summary>Désérialisé depuis Question.PayloadJson pour une manche "image-guess".</summary>
public class ImageGuessQuestionPayload
{
    public string ImageUrl { get; set; } = "";
    public List<string> AcceptedAnswers { get; set; } = [];
}
