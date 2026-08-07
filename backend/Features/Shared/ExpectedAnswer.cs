using System.Text.Json;

namespace QuizParty.Api.Features.Shared;

/// <summary>Une réponse distincte attendue parmi potentiellement plusieurs pour une même question
/// (ex: "cite 2 pays d'Europe" → 2 ExpectedAnswer). Remplace l'ancien AcceptedAnswers plat, qui ne
/// représentait que des synonymes d'UNE seule réponse — conservé en legacy pour la rétrocompatibilité
/// de lecture des quiz existants (voir chaque *QuestionPayload.ExpectedAnswersOrLegacy()).</summary>
public class ExpectedAnswer
{
    /// <summary>Synonymes tolérés pour CETTE réponse précise (même rôle que l'ancien AcceptedAnswers,
    /// mais à l'échelle d'une seule réponse parmi plusieurs).</summary>
    public List<string> AcceptedVariants { get; set; } = [];

    /// <summary>Null = utilise le barème uniforme de la manche. Renseigné = cette réponse précise
    /// rapporte ce montant, indépendamment des autres (mode "points personnalisés par réponse").</summary>
    public int? Points { get; set; }
}

/// <summary>Associe les réponses soumises par un joueur aux réponses attendues d'une question, pour
/// toute feature multi-réponses (qa-text, zoom-image, blind-test, image-guess).</summary>
public static class ExpectedAnswerMatching
{
    public record MatchResult(int PointsAwarded, bool AllMatched);

    /// <summary>Pour chaque réponse soumise (dans l'ordre d'envoi), cherche parmi les réponses attendues
    /// encore libres celle dont un synonyme toléré matche (AnswerMatcher.IsMatch) — en cas d'ambiguïté
    /// (plusieurs réponses libres correspondent), retient celle qui rapporte le plus de points, toujours
    /// favorable au joueur. Chaque réponse attendue n'est réclamable qu'une seule fois, pour empêcher de
    /// taper deux fois la même bonne réponse dans deux champs différents.</summary>
    public static MatchResult Match(List<ExpectedAnswer> expectedAnswers, List<string> submittedAnswers, Func<ExpectedAnswer, int> effectivePoints)
    {
        var unclaimed = expectedAnswers.ToList();
        var totalPoints = 0;

        foreach (var submitted in submittedAnswers)
        {
            var match = unclaimed
                .Where(e => e.AcceptedVariants.Any(variant => AnswerMatcher.IsMatch(variant, submitted)))
                .OrderByDescending(effectivePoints)
                .FirstOrDefault();

            if (match is null)
            {
                continue;
            }

            totalPoints += effectivePoints(match);
            unclaimed.Remove(match);
        }

        return new MatchResult(totalPoints, expectedAnswers.Count > 0 && unclaimed.Count == 0);
    }

    /// <summary>Barème visible du joueur avant de répondre : un point par réponse attendue, dans l'ordre
    /// configuré — jamais le contenu des réponses elles-mêmes.</summary>
    public static List<int> BuildPointsArray(List<ExpectedAnswer> expectedAnswers, Func<ExpectedAnswer, int> effectivePoints) =>
        expectedAnswers.Select(effectivePoints).ToList();

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    /// <summary>Une seule réponse attendue : rawAnswer est le texte brut tel quel (comportement historique,
    /// inchangé). Plusieurs réponses attendues : rawAnswer est un tableau JSON de N chaînes, encodé côté
    /// client dès qu'il connaît expectedAnswerCount &gt; 1 (voir QaEngine/ZoomImageEngine.BuildPublicPayloadJson).</summary>
    public static List<string> SplitRawAnswer(string rawAnswer, int expectedCount)
    {
        if (expectedCount <= 1)
        {
            return [rawAnswer];
        }

        try
        {
            return JsonSerializer.Deserialize<List<string>>(rawAnswer, JsonOptions) ?? [rawAnswer];
        }
        catch (JsonException)
        {
            return [rawAnswer];
        }
    }
}
