using QuizParty.Api.Features.Shared;

namespace QuizParty.Api.Features.ImageGuess;

/// <summary>Désérialisé depuis Question.PayloadJson pour une manche "image-guess".</summary>
public class ImageGuessQuestionPayload
{
    public string ImageUrl { get; set; } = "";

    /// <summary>Legacy : synonymes d'UNE seule réponse, avant l'introduction des réponses multiples.
    /// Plus jamais écrit par l'éditeur, conservé pour la rétrocompatibilité — voir ExpectedAnswersOrLegacy().</summary>
    public List<string> AcceptedAnswers { get; set; } = [];

    public List<ExpectedAnswer> ExpectedAnswers { get; set; } = [];

    public List<ExpectedAnswer> ExpectedAnswersOrLegacy() =>
        ExpectedAnswers.Count > 0
            ? ExpectedAnswers
            : AcceptedAnswers.Count > 0
                ? [new ExpectedAnswer { AcceptedVariants = AcceptedAnswers, Points = null }]
                : [];
}
