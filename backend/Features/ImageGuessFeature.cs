namespace QuizParty.Api.Features;

/// <summary>Feature "Image à deviner" : une image fixe s'affiche intégralement, même mécanique que Question / Réponse.</summary>
public class ImageGuessFeature : IQuizFeature
{
    public string TypeKey => "image-guess";
    public string DisplayName => "Image à deviner";
    public string Description => "Une image s'affiche intégralement (sans zoom progressif) ; les joueurs répondent comme pour une Question / Réponse classique.";
}
