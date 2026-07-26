using System.Globalization;
using System.Text;

namespace QuizParty.Api.Features.Zoom;

public record ZoomAnswerEvaluation(bool? IsCorrect, int PointsAwarded);

/// <summary>
/// Moteur d'exécution de la feature "zoom-image" (section 6). Le serveur est seul autoritaire
/// sur le temps : le palier actif est toujours recalculé depuis CurrentQuestionStartedAt, jamais
/// transmis par le client.
/// </summary>
public class ZoomImageEngine
{
    /// <summary>Distance de Levenshtein maximale tolérée en mode Auto (section 9 : réglage par défaut).</summary>
    private const int MaxLevenshteinDistance = 2;

    public ZoomRuntimeState ComputeState(ZoomRoundConfig config, DateTime questionStartedAt, DateTime? pausedAt, DateTime now)
    {
        var elapsedSeconds = ComputeElapsedSeconds(questionStartedAt, pausedAt, now);
        return ComputeStateAtElapsed(config, elapsedSeconds);
    }

    public ZoomAnswerEvaluation Evaluate(
        ZoomRoundConfig config,
        ZoomQuestionPayload payload,
        string rawAnswer,
        DateTime questionStartedAt,
        DateTime? pausedAt,
        DateTime submittedAt)
    {
        var elapsedSeconds = ComputeElapsedSeconds(questionStartedAt, pausedAt, submittedAt);
        var state = ComputeStateAtElapsed(config, elapsedSeconds);

        if (config.ValidationMode == "Manual")
        {
            return new ZoomAnswerEvaluation(null, 0);
        }

        var isCorrect = payload.AcceptedAnswers.Any(accepted => IsMatch(accepted, rawAnswer));
        return new ZoomAnswerEvaluation(isCorrect, isCorrect ? state.CurrentPoints : 0);
    }

    /// <summary>Points qu'une réponse correcte rapporterait si elle était validée manuellement maintenant (a posteriori, sur le palier actif au moment de l'envoi).</summary>
    public int PointsForElapsedSeconds(ZoomRoundConfig config, double elapsedSeconds) =>
        ComputeStateAtElapsed(config, elapsedSeconds).CurrentPoints;

    public double ComputeElapsedSeconds(DateTime questionStartedAt, DateTime? pausedAt, DateTime now)
    {
        var effectiveNow = pausedAt ?? now;
        return Math.Max(0, (effectiveNow - questionStartedAt).TotalSeconds);
    }

    /// <summary>Durée cumulée de tous les paliers de zoom, avant le temps supplémentaire (AnswerTimeSeconds).</summary>
    public double TotalZoomDurationSeconds(ZoomRoundConfig config) => config.ZoomSteps.Sum(s => s.DurationSeconds);

    private static ZoomRuntimeState ComputeStateAtElapsed(ZoomRoundConfig config, double elapsedSeconds)
    {
        var cumulative = 0.0;
        ZoomStep? activeStep = null;
        var secondsRemainingInStep = 0;

        foreach (var step in config.ZoomSteps)
        {
            if (elapsedSeconds < cumulative + step.DurationSeconds)
            {
                activeStep = step;
                secondsRemainingInStep = (int)Math.Ceiling(cumulative + step.DurationSeconds - elapsedSeconds);
                break;
            }
            cumulative += step.DurationSeconds;
        }

        // Paliers épuisés : l'image reste au niveau final, les points restent plafonnés à ceux du dernier palier.
        var lastStep = config.ZoomSteps.Count > 0 ? config.ZoomSteps[^1] : null;
        var currentLevel = activeStep?.Level ?? config.FinalLevel;
        var currentPoints = activeStep?.Points ?? lastStep?.Points ?? 0;

        // AnswerTimeSeconds est un temps SUPPLÉMENTAIRE accordé une fois la séquence de zoom
        // terminée (pas un plafond concurrent) : sinon un answerTimeSeconds plus court que la
        // durée totale des paliers coupait le dézoom en plein milieu avant que le joueur ait
        // même vu l'image se révéler complètement.
        var totalZoomDuration = config.ZoomSteps.Sum(s => s.DurationSeconds);
        var totalRoundDuration = totalZoomDuration + config.AnswerTimeSeconds;

        var isAnswerWindowOpen = elapsedSeconds < totalRoundDuration;
        var shouldAutoAdvance = config.AutoAdvance && !isAnswerWindowOpen;

        return new ZoomRuntimeState(currentLevel, currentPoints, secondsRemainingInStep, isAnswerWindowOpen, shouldAutoAdvance);
    }

    private static bool IsMatch(string accepted, string submitted)
    {
        var a = Normalize(accepted);
        var b = Normalize(submitted);

        if (a.Length == 0 || b.Length == 0)
        {
            return a == b;
        }

        return LevenshteinDistance(a, b) <= MaxLevenshteinDistance;
    }

    private static string Normalize(string value)
    {
        var trimmed = value.Trim().ToLowerInvariant();
        var decomposed = trimmed.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder();

        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(c);
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    private static int LevenshteinDistance(string a, string b)
    {
        var costs = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++)
        {
            costs[j] = j;
        }

        for (var i = 1; i <= a.Length; i++)
        {
            costs[0] = i;
            var previousDiagonal = i - 1;
            for (var j = 1; j <= b.Length; j++)
            {
                var previousDiagonalSave = costs[j];
                costs[j] = a[i - 1] == b[j - 1]
                    ? previousDiagonal
                    : 1 + Math.Min(previousDiagonal, Math.Min(costs[j], costs[j - 1]));
                previousDiagonal = previousDiagonalSave;
            }
        }

        return costs[b.Length];
    }
}
