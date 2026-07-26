namespace QuizParty.Api.Features;

/// <summary>
/// Métadonnées d'une feature de quiz (section 4 de la spec). Le contrat d'exécution
/// (StartRound/NextQuestion/SubmitAnswer/ComputeAutoScore/GetPreviewState) sera ajouté
/// en Phase 2 avec le moteur de jeu — Phase 1 n'a besoin que d'identifier et lister
/// les types de manche disponibles pour l'éditeur.
/// </summary>
public interface IQuizFeature
{
    string TypeKey { get; }
    string DisplayName { get; }
    string Description { get; }
}
