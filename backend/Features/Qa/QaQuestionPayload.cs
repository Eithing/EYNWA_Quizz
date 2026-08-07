using QuizParty.Api.Features.Shared;

namespace QuizParty.Api.Features.Qa;

/// <summary>Désérialisé depuis Question.PayloadJson pour une manche "qa-text".</summary>
public class QaQuestionPayload
{
    public string QuestionText { get; set; } = "";

    /// <summary>Legacy : synonymes d'UNE seule réponse, avant l'introduction des réponses multiples.
    /// Plus jamais écrit par l'éditeur, conservé uniquement pour que les quiz existants continuent de
    /// se lire correctement — voir ExpectedAnswersOrLegacy().</summary>
    public List<string> AcceptedAnswers { get; set; } = [];

    public List<ExpectedAnswer> ExpectedAnswers { get; set; } = [];

    /// <summary>ExpectedAnswers si renseigné, sinon reconstruit depuis l'ancien AcceptedAnswers plat
    /// (une seule réponse attendue, synonymes = l'ancienne liste, points = barème uniforme de la
    /// manche) — permet aux quiz créés avant les réponses multiples de continuer à fonctionner sans
    /// migration de données.</summary>
    public List<ExpectedAnswer> ExpectedAnswersOrLegacy() =>
        ExpectedAnswers.Count > 0
            ? ExpectedAnswers
            : AcceptedAnswers.Count > 0
                ? [new ExpectedAnswer { AcceptedVariants = AcceptedAnswers, Points = null }]
                : [];
}
