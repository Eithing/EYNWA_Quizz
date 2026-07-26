namespace QuizParty.Api.Models;

public class Round
{
    public int Id { get; set; }
    public int QuizId { get; set; }
    public Quiz? Quiz { get; set; }

    public int Order { get; set; }

    /// <summary>Clé de la feature (ex: "zoom-image") résolue via le FeatureRegistry (Phase 1+).</summary>
    public required string FeatureTypeKey { get; set; }

    public required string Title { get; set; }

    /// <summary>Configuration de la manche, schéma libre propre à la feature.</summary>
    public required string ConfigJson { get; set; }

    /// <summary>Manche thématique : réservée à un seul joueur, choisi en direct par le GM au lancement de la manche (section ciblage, générique à toutes les features).</summary>
    public bool RequiresTargetPlayer { get; set; }

    public List<Question> Questions { get; set; } = [];
}
