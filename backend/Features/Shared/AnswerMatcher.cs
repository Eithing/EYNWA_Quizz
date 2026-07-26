using System.Globalization;
using System.Text;

namespace QuizParty.Api.Features.Shared;

/// <summary>Comparaison de réponses tolérante aux fautes (accents, casse, distance de Levenshtein), partagée par toutes les features à validation "Auto".</summary>
public static class AnswerMatcher
{
    /// <summary>Distance de Levenshtein maximale tolérée (section 9 : réglage par défaut).</summary>
    private const int MaxLevenshteinDistance = 2;

    public static bool IsMatch(string accepted, string submitted)
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
