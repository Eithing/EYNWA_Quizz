namespace QuizParty.Api.Features.ClosestGuess;

/// <summary>Désérialisé depuis Round.ConfigJson pour une manche "closest-guess".</summary>
public class ClosestGuessRoundConfig
{
    /// <summary>"Auto" : le classement se calcule automatiquement dès la fermeture de la fenêtre de réponse.
    /// "Manual" : le GM déclenche la révélation quand il le souhaite (bouton dédié).</summary>
    public string ValidationMode { get; set; } = "Auto";
    public int AnswerTimeSeconds { get; set; } = 30;

    /// <summary>Points pour la meilleure estimation (ou la meilleure moyenne d'équipe) si RankBasedScoring est faux.</summary>
    public int Points { get; set; } = 100;

    /// <summary>Si vrai, dégressif : la meilleure estimation prend RankMaxPoints, la suivante un peu moins, etc.
    /// Si faux, seule la meilleure estimation (le rang 0) marque Points.</summary>
    public bool RankBasedScoring { get; set; }
    public int RankMaxPoints { get; set; } = 100;
    public int RankPointsDecrement { get; set; } = 10;
}
