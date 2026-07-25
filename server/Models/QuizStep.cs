namespace Server.Models;

public enum StepType
{
    ZoomProgressif,
    BlindTest,
    GeoGamer,
    Memorisation,
    DefileSuccessif,
    QuestionDirecte,
    TuPreferes,
    ConnaissanceCroisee,
    LePanel
}

public class QuizStep
{
    public int Id { get; set; }
    public int QuizId { get; set; }
    public Quiz? Quiz { get; set; }

    public int OrderIndex { get; set; }
    public StepType Type { get; set; }
    public required string Title { get; set; }

    /// <summary>Config spécifique au type d'épreuve, sérialisée en JSON (schéma variable selon Type).</summary>
    public required string ConfigJson { get; set; }
}
