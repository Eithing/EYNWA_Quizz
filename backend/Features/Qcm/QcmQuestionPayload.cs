namespace QuizParty.Api.Features.Qcm;

public class QcmOption
{
    public string Id { get; set; } = "";
    public string Content { get; set; } = "";
    public bool IsCorrect { get; set; }

    /// <summary>Uniquement pertinent si IsCorrect. Null = utilise le barème uniforme de la manche
    /// (mode "points personnalisés" sinon, voir QcmRoundConfig.PointsMode).</summary>
    public int? Points { get; set; }
}

/// <summary>Désérialisé depuis Question.PayloadJson pour une manche "multiple-choice".</summary>
public class QcmQuestionPayload
{
    public string QuestionText { get; set; } = "";
    public List<QcmOption> Options { get; set; } = [];
}
