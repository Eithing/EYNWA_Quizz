using QuizParty.Api.Features.Shared;

namespace QuizParty.Api.Features.BlindTest;

/// <summary>Désérialisé depuis Question.PayloadJson pour une manche "blind-test".</summary>
public class BlindTestQuestionPayload
{
    public string AudioUrl { get; set; } = "";

    /// <summary>Legacy : synonymes d'UNE seule réponse, avant l'introduction des réponses multiples.
    /// Plus jamais écrit par l'éditeur, conservé pour la rétrocompatibilité — voir ExpectedAnswersOrLegacy().</summary>
    public List<string> AcceptedAnswers { get; set; } = [];

    public List<ExpectedAnswer> ExpectedAnswers { get; set; } = [];

    /// <summary>Commentaire/consigne optionnel affiché à côté du lecteur audio (ex: "Soyez précis").
    /// Vide par défaut.</summary>
    public string Comment { get; set; } = "";

    public List<ExpectedAnswer> ExpectedAnswersOrLegacy() =>
        ExpectedAnswers.Count > 0
            ? ExpectedAnswers
            : AcceptedAnswers.Count > 0
                ? [new ExpectedAnswer { AcceptedVariants = AcceptedAnswers, Points = null }]
                : [];
}
