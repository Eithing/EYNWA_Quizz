namespace QuizParty.Api.Features;

/// <summary>Feature "Au plus proche" : les joueurs soumettent une estimation numérique, le classement (et les
/// points) se calcule une fois tout le monde répondu, par proximité à la vraie valeur.</summary>
public class ClosestGuessFeature : IQuizFeature
{
    public string TypeKey => "closest-guess";
    public string DisplayName => "Au plus proche";
    public string Description => "Les joueurs estiment un nombre (ex: une taille, une date, un prix) ; le classement se fait par proximité à la vraie valeur une fois tout le monde répondu.";
}
