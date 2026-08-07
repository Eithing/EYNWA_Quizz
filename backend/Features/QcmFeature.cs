namespace QuizParty.Api.Features;

/// <summary>Feature "Choix Multiple" (QCM) : le GM définit des options dont une ou plusieurs sont
/// correctes, chacune avec son propre nombre de points ; le joueur coche jusqu'à N réponses (N = nombre
/// de bonnes réponses de la question, jamais plus).</summary>
public class QcmFeature : IQuizFeature
{
    public string TypeKey => "multiple-choice";
    public string DisplayName => "Choix Multiple";
    public string Description => "Les joueurs cochent une ou plusieurs bonnes réponses parmi une liste d'options, chacune avec son propre barème de points.";
}
