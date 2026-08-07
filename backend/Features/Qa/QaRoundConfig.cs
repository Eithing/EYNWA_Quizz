namespace QuizParty.Api.Features.Qa;

/// <summary>Désérialisé depuis Round.ConfigJson pour une manche "qa-text".</summary>
public class QaRoundConfig
{
    public string ValidationMode { get; set; } = "Auto";
    public bool AutoAdvance { get; set; } = true;
    public int AnswerTimeSeconds { get; set; } = 20;

    /// <summary>Points fixes pour une réponse correcte — pas de dégressivité dans le temps, contrairement à zoom-image.</summary>
    public int Points { get; set; } = 100;

    /// <summary>Question de rapidité : les joueurs buzzent pour obtenir la main, le GM valide toujours à l'oral (pas de saisie écrite).</summary>
    public bool BuzzerMode { get; set; }

    /// <summary>Si vrai, un joueur qui se trompe peut retenter sa chance tant que la fenêtre de réponse est ouverte
    /// (en mode buzzer : peut re-buzzer, sous réserve de BuzzerRetryCooldownSeconds ; à l'écrit, sous réserve de
    /// RetryCooldownSeconds).</summary>
    public bool AllowRetry { get; set; }

    /// <summary>Mode buzzer uniquement : délai en secondes avant qu'un joueur éliminé sur cette question puisse re-buzzer.
    /// Ignoré si AllowRetry est faux. Sans effet sur la réponse écrite classique (voir RetryCooldownSeconds).</summary>
    public int BuzzerRetryCooldownSeconds { get; set; }

    /// <summary>Réponse écrite classique uniquement : délai en secondes avant qu'un joueur ayant répondu faux
    /// puisse retenter sa chance. Ignoré si AllowRetry est faux ou en mode buzzer (voir BuzzerRetryCooldownSeconds).</summary>
    public int RetryCooldownSeconds { get; set; }

    /// <summary>Si vrai, les points ne sont plus fixes mais dépendent du rang d'arrivée parmi les bonnes réponses
    /// (1er = RankMaxPoints, puis -RankPointsDecrement par rang suivant, plancher à 0). Sans effet en mode buzzer
    /// (une seule bonne réponse possible par question, le rang est toujours 0). Mutuellement exclusif avec
    /// PointsMode == "PerAnswer" au niveau de l'éditeur (un seul sélecteur de mode de scoring) — les deux champs
    /// restent indépendants ici pour ne pas casser les configs déjà enregistrées.</summary>
    public bool RankBasedScoring { get; set; }
    public int RankMaxPoints { get; set; } = 100;
    public int RankPointsDecrement { get; set; } = 10;

    /// <summary>"Uniform" (défaut, chaque réponse trouvée rapporte Points) ou "PerAnswer" (chaque réponse
    /// attendue de la question porte son propre nombre de points, voir ExpectedAnswer.Points).</summary>
    public string PointsMode { get; set; } = "Uniform";
}
