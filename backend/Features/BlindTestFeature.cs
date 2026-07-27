namespace QuizParty.Api.Features;

/// <summary>Feature "Blind Test" : un extrait audio est joué (rejouable), les joueurs devinent le titre/artiste/etc., même mécanique que Question / Réponse.</summary>
public class BlindTestFeature : IQuizFeature
{
    public string TypeKey => "blind-test";
    public string DisplayName => "Blind Test";
    public string Description => "Un extrait audio est joué aux joueurs (rejouable à volonté) ; ils répondent comme pour une Question / Réponse classique.";
}
