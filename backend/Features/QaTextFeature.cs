namespace QuizParty.Api.Features;

/// <summary>Feature "Question / Réponse" : question écrite, réponse validée à l'écrit (auto) ou à l'oral (GM).</summary>
public class QaTextFeature : IQuizFeature
{
    public string TypeKey => "qa-text";
    public string DisplayName => "Question / Réponse";
    public string Description => "Une question écrite s'affiche ; les joueurs répondent, à l'écrit (vérifié automatiquement) ou à l'oral (validé par l'hôte).";
}
