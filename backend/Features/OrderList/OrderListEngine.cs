using System.Text.Json;
using QuizParty.Api.Features.Shared;

namespace QuizParty.Api.Features.OrderList;

/// <summary>
/// Moteur d'exécution de la feature "order-list" (glisser-déposer pour retrouver l'ordre correct).
/// Le score ne dépend jamais des autres joueurs (contrairement à closest-guess) : chaque réponse se
/// note indépendamment via Evaluate(), dès qu'elle est finalisée (clic "Valider" ou fermeture de la
/// fenêtre) — voir IFeatureEngine.FinalizesPendingAnswersOnAdvance et
/// SessionsController.FinalizeIndependentPendingAnswersAsync.
/// </summary>
public class OrderListEngine : IFeatureEngine
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly JsonSerializerOptions PublicJsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public string FeatureTypeKey => "order-list";

    public FeatureRuntimeState ComputeState(string configJson, DateTime questionStartedAt, DateTime? pausedAt, DateTime now)
    {
        var config = ParseConfig(configJson);
        var elapsedSeconds = SessionTiming.ComputeElapsedSeconds(questionStartedAt, pausedAt, now);
        var secondsRemaining = (int)Math.Ceiling(Math.Max(0, config.AnswerTimeSeconds - elapsedSeconds));
        var isAnswerWindowOpen = elapsedSeconds < config.AnswerTimeSeconds;
        // Comme closest-guess : jamais d'avance automatique, le classement final (qui a le mieux
        // enchaîné) doit rester affiché le temps que le GM le décide via "Suivant".
        var shouldAutoAdvance = false;

        return new FeatureRuntimeState(1, config.PointsPerChainedItem, secondsRemaining, secondsRemaining, isAnswerWindowOpen, shouldAutoAdvance, elapsedSeconds);
    }

    public int PointsForElapsedSeconds(string configJson, double elapsedSeconds) => 0; // sans objet : scoring uniquement à la finalisation, jamais lié au temps écoulé.

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
        var chainIds = ComputeChainItemIds(payload, rawAnswer);
        var points = chainIds.Count * config.PointsPerChainedItem;
        var isCorrect = payload.Items.Count > 0 && chainIds.Count == payload.Items.Count;

        return new FeatureAnswerEvaluation(isCorrect, points);
    }

    public bool IsManualValidation(string configJson) => false;

    public string BuildPublicPayloadJson(string payloadJson)
    {
        var payload = ParsePayload(payloadJson);

        // Défensif : l'ordre "affiché" réel en jeu vient du brouillon persisté côté serveur
        // (SessionsController), jamais de ce payload — mais on mélange quand même ici pour ne
        // jamais exposer l'ordre correct tel quel, même par accident.
        var shuffled = payload.Items.ToList();
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
                contentType = payload.ContentType,
                items = shuffled.Select(it => new { id = it.Id, content = it.Content })
            },
            PublicJsonOptions);
    }

    public double GetFastForwardTargetElapsedSeconds(string configJson) => ParseConfig(configJson).AnswerTimeSeconds;

    public bool FinalizesPendingAnswersOnAdvance(string configJson) => true;

    /// <summary>Indices (dans payload.Items, ordre correct) des items dont l'id apparaît dans la plus
    /// longue chaîne bien enchaînée de rawAnswer — exposé en static pour que le contrôleur puisse
    /// construire l'affichage de révélation (quels items étaient "bien placés") sans dupliquer la
    /// logique de correspondance id → position correcte.</summary>
    public static List<string> ComputeChainItemIds(string payloadJson, string rawAnswerJson) =>
        ComputeChainItemIds(ParsePayload(payloadJson), rawAnswerJson);

    private static List<string> ComputeChainItemIds(OrderListQuestionPayload payload, string rawAnswerJson)
    {
        var correctPositionByItemId = payload.Items
            .Select((item, index) => (item.Id, index))
            .ToDictionary(x => x.Id, x => x.index);

        List<string>? submittedOrder;
        try
        {
            submittedOrder = JsonSerializer.Deserialize<List<string>>(rawAnswerJson, JsonOptions);
        }
        catch (JsonException)
        {
            submittedOrder = null;
        }

        if (submittedOrder is null || submittedOrder.Count == 0)
        {
            return [];
        }

        // Ids inconnus (ne devrait pas arriver hors bug/manipulation du client) ignorés plutôt que
        // de faire planter le calcul de score.
        var known = submittedOrder
            .Select((id, submittedIndex) => (id, submittedIndex, correctPosition: correctPositionByItemId.GetValueOrDefault(id, -1)))
            .Where(x => x.correctPosition >= 0)
            .ToList();

        var chainOfKnownIndices = LongestIncreasingSubsequence.ComputeChainIndices(known.Select(x => x.correctPosition).ToList());

        return chainOfKnownIndices.Select(i => known[i].id).ToList();
    }

    private static OrderListRoundConfig ParseConfig(string configJson) =>
        JsonSerializer.Deserialize<OrderListRoundConfig>(configJson, JsonOptions) ?? new OrderListRoundConfig();

    private static OrderListQuestionPayload ParsePayload(string payloadJson) =>
        JsonSerializer.Deserialize<OrderListQuestionPayload>(payloadJson, JsonOptions) ?? new OrderListQuestionPayload();
}
