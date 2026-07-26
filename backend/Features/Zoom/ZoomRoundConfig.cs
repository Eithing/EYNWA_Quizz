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
}
