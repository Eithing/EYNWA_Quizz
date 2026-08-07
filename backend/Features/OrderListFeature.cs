namespace QuizParty.Api.Features;

/// <summary>Feature "Ordonne la liste" : les joueurs glissent-déposent une liste d'items (texte, image ou
/// son) pour retrouver l'ordre correct défini par le GM ; le score dépend du plus long enchaînement déjà
/// dans le bon ordre relatif, pas d'une comparaison stricte position par position.</summary>
public class OrderListFeature : IQuizFeature
{
    public string TypeKey => "order-list";
    public string DisplayName => "Ordonne la liste";
    public string Description => "Les joueurs (ou l'équipe, en temps réel) glissent-déposent une liste d'items pour retrouver l'ordre correct (ex: du plus récent au plus ancien).";
}
