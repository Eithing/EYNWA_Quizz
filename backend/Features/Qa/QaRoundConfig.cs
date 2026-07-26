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
}
