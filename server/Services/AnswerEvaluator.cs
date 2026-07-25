using System.Text.Json;

namespace Server.Services;

public record AnswerEvaluationResult(bool IsCorrect, int PointsAwarded);

public static class AnswerEvaluator
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static AnswerEvaluationResult Evaluate(string configJson, string submittedAnswer)
    {
        var config = JsonSerializer.Deserialize<StepScoringConfig>(configJson, JsonOptions) ?? new StepScoringConfig();
        var expected = config.Answer?.Trim() ?? string.Empty;
        var submitted = submittedAnswer.Trim();

        var isCorrect = false;
        if (expected.Length > 0)
        {
            var tolerance = config.ToleranceRatio ?? 1.0;
            var similarity = ComputeSimilarity(expected, submitted);
            isCorrect = similarity >= tolerance;
        }

        var points = isCorrect
            ? config.Scoring.BaremeParPalier?.FirstOrDefault() is > 0 and var pts ? pts : 10
            : config.Scoring.Type == "MALUS" ? -(config.Scoring.MalusParErreur ?? 1) : 0;

        return new AnswerEvaluationResult(isCorrect, points);
    }

    private static double ComputeSimilarity(string expected, string submitted)
    {
        var a = expected.ToLowerInvariant();
        var b = submitted.ToLowerInvariant();

        if (a == b)
        {
            return 1.0;
        }
        if (a.Length == 0 || b.Length == 0)
        {
            return 0.0;
        }

        var distance = LevenshteinDistance(a, b);
        var maxLength = Math.Max(a.Length, b.Length);
        return 1.0 - (double)distance / maxLength;
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
