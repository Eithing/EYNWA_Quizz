namespace QuizParty.Api.Features.Qcm;

/// <summary>Désérialisé depuis Round.ConfigJson pour une manche "multiple-choice". Volontairement plus
/// léger que QaRoundConfig : pas de buzzer/retry/validation manuelle — cocher des cases n'a pas
/// d'ambiguïté à juger, la correction est toujours automatique et immédiate.</summary>
public class QcmRoundConfig
{
    public int AnswerTimeSeconds { get; set; } = 30;
    public bool AutoAdvance { get; set; }

    /// <summary>Barème uniforme : chaque bonne réponse cochée rapporte ce montant. Ignoré si
    /// PointsMode == "PerAnswer" (chaque option correcte porte alors son propre montant, voir
    /// QcmOption.Points).</summary>
    public int Points { get; set; } = 100;

    public string PointsMode { get; set; } = "Uniform";
}
