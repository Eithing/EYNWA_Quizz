namespace QuizParty.Api.Features;

/// <summary>Feature "À quoi pense l'autre" : un joueur désigné répond en privé à une question ouverte,
/// un autre joueur (ou une équipe) tente ensuite de deviner sa réponse exacte.</summary>
public class PartnerGuessFeature : IQuizFeature
{
    public string TypeKey => "partner-guess";
    public string DisplayName => "À quoi pense l'autre";
    public string Description => "Un joueur désigné par l'hôte répond en privé à la question ; un autre joueur (ou une équipe) essaie ensuite de deviner sa réponse exacte.";
}
