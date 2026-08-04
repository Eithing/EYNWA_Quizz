namespace QuizParty.Api.Features.Zoom;

public class ZoomStep
{
    public double Level { get; set; }
    public int DurationSeconds { get; set; }
    public int Points { get; set; }
}

/// <summary>Désérialisé depuis Round.ConfigJson pour une manche "zoom-image" (section 6 de la spec).</summary>
public class ZoomRoundConfig
{
    public string ValidationMode { get; set; } = "Auto";
    public bool AutoAdvance { get; set; } = true;
    public int AnswerTimeSeconds { get; set; } = 30;
    public List<ZoomStep> ZoomSteps { get; set; } = [];
    public double FinalLevel { get; set; } = 1;

    /// <summary>Si vrai, un joueur qui se trompe peut retenter sa chance tant que la fenêtre de réponse est ouverte
    /// (sous réserve de RetryCooldownSeconds).</summary>
    public bool AllowRetry { get; set; }

    /// <summary>Délai en secondes avant qu'un joueur ayant répondu faux puisse retenter sa chance. Ignoré si AllowRetry est faux.</summary>
    public int RetryCooldownSeconds { get; set; }

    /// <summary>Si vrai, les points ne dépendent plus du palier de zoom mais du rang d'arrivée parmi les bonnes réponses
    /// (1er = RankMaxPoints, puis -RankPointsDecrement par rang suivant, plancher à 0).</summary>
    public bool RankBasedScoring { get; set; }
    public int RankMaxPoints { get; set; } = 100;
    public int RankPointsDecrement { get; set; } = 10;
}
