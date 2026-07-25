namespace Server.Services;

public class ScoringConfigPart
{
    public string Type { get; set; } = "FIXE";
    public List<int>? BaremeParPalier { get; set; }
    public int? MalusParErreur { get; set; }
}

/// <summary>Sous-ensemble des champs de ConfigJson utiles côté serveur pour valider une réponse (indépendant du type d'épreuve).</summary>
public class StepScoringConfig
{
    public string? Answer { get; set; }
    public double? ToleranceRatio { get; set; }
    public ScoringConfigPart Scoring { get; set; } = new();
}
