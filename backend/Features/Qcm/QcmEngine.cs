using System.Text.Json;
using QuizParty.Api.Features.Shared;

namespace QuizParty.Api.Features.Qcm;

/// <summary>
/// Moteur d'exécution de la feature "multiple-choice" (choix multiple à cases à cocher). Le joueur ne
/// peut jamais cocher plus de cases qu'il n'y a de bonnes réponses (maxSelectable) — c'est ce plafond,
/// pas une pénalité, qui empêche de tout cocher pour garantir les points (décision produit).
/// </summary>
public class QcmEngine : IFeatureEngine
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly JsonSerializerOptions PublicJsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public string FeatureTypeKey => "multiple-choice";

    public FeatureRuntimeState ComputeState(string configJson, DateTime questionStartedAt, DateTime? pausedAt, DateTime now)
    {
        var config = ParseConfig(configJson);
        var elapsedSeconds = SessionTiming.ComputeElapsedSeconds(questionStartedAt, pausedAt, now);
        var secondsRemaining = (int)Math.Ceiling(Math.Max(0, config.AnswerTimeSeconds - elapsedSeconds));
        var isAnswerWindowOpen = elapsedSeconds < config.AnswerTimeSeconds;
        var shouldAutoAdvance = config.AutoAdvance && !isAnswerWindowOpen;

        return new FeatureRuntimeState(1, config.Points, secondsRemaining, secondsRemaining, isAnswerWindowOpen, shouldAutoAdvance, elapsedSeconds);
    }

    public int PointsForElapsedSeconds(string configJson, double elapsedSeconds) => 0; // sans objet : jamais de scoring dépendant du temps ici.

    public FeatureAnswerEvaluation Evaluate(
        string configJson,
        string payloadJson,
        string rawAnswer,
        DateTime questionStartedAt,
        DateTime? pausedAt,
        DateTime submittedAt)
    {
        var config = ParseConfig(configJson);
        var payload = ParsePayload(payloadJson);
        var correctOptions = payload.Options.Where(o => o.IsCorrect).ToList();
        var maxSelectable = correctOptions.Count;

        List<string> selectedIds;
        try
        {
            selectedIds = JsonSerializer.Deserialize<List<string>>(rawAnswer, JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            selectedIds = [];
        }

        // Un client qui contournerait le plafond côté UI (ou un bug) ne doit jamais pouvoir marquer de
        // points en cochant plus de cases qu'il n'y a de bonnes réponses.
        if (selectedIds.Count > maxSelectable)
        {
            return new FeatureAnswerEvaluation(false, 0);
        }

        var selectedSet = selectedIds.ToHashSet();
        var correctSelected = correctOptions.Where(o => selectedSet.Contains(o.Id)).ToList();
        var points = correctSelected.Sum(o => o.Points ?? config.Points);
        // Correct "parfait" seulement si l'ensemble coché est EXACTEMENT l'ensemble des bonnes réponses.
        var isCorrect = maxSelectable > 0 && correctSelected.Count == maxSelectable && selectedIds.Count == maxSelectable;

        return new FeatureAnswerEvaluation(isCorrect, points);
    }

    public bool IsManualValidation(string configJson) => false;

    public string BuildPublicPayloadJson(string payloadJson) => BuildPublicPayloadJson(payloadJson, "{}");

    public string BuildPublicPayloadJson(string payloadJson, string configJson)
    {
        var config = ParseConfig(configJson);
        var payload = ParsePayload(payloadJson);
        var correctOptions = payload.Options.Where(o => o.IsCorrect).ToList();

        // Options mélangées, jamais IsCorrect/Points par option — seul le nombre de bonnes réponses et
        // leurs valeurs de points (sans association à une option précise) sont visibles avant de répondre.
        var shuffled = payload.Options.ToList();
        var random = Random.Shared;
        for (var i = shuffled.Count - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (shuffled[i], shuffled[j]) = (shuffled[j], shuffled[i]);
        }

        return JsonSerializer.Serialize(
            new
            {
                questionText = payload.QuestionText,
                options = shuffled.Select(o => new { id = o.Id, content = o.Content }),
                maxSelectable = correctOptions.Count,
                correctOptionPoints = correctOptions.Select(o => o.Points ?? config.Points).ToList()
            },
            PublicJsonOptions);
    }

    public double GetFastForwardTargetElapsedSeconds(string configJson) => ParseConfig(configJson).AnswerTimeSeconds;

    private static QcmRoundConfig ParseConfig(string configJson) =>
        JsonSerializer.Deserialize<QcmRoundConfig>(configJson, JsonOptions) ?? new QcmRoundConfig();

    private static QcmQuestionPayload ParsePayload(string payloadJson) =>
        JsonSerializer.Deserialize<QcmQuestionPayload>(payloadJson, JsonOptions) ?? new QcmQuestionPayload();
}
