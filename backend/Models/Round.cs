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

    /// <summary>Manche réservée à une sélection de joueurs et/ou d'équipes, choisie en direct par le GM au
    /// lancement de la manche (générique à toutes les features) — remplace l'ancien ciblage à un seul joueur.</summary>
    public bool RestrictsParticipants { get; set; }

    /// <summary>Manche "à thèmes" : ne porte pas de questions directement, contient des sous-manches
    /// (Questions/ConfigJson/FeatureTypeKey ignorés sur cette ligne) choisies en direct par les joueurs.</summary>
    public bool IsThemePicker { get; set; }

    /// <summary>Non-null si cette manche est une sous-manche (thème) d'une manche à thèmes.</summary>
    public int? ParentRoundId { get; set; }
    public Round? Parent { get; set; }
    public List<Round> SubRounds { get; set; } = [];

    public List<Question> Questions { get; set; } = [];
}
