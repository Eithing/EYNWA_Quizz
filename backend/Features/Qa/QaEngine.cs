using System.Text.Json;
using QuizParty.Api.Features.Shared;

namespace QuizParty.Api.Features.Qa;

/// <summary>
/// Moteur d'exécution de la feature "qa-text" (question écrite / réponse attendue). Contrairement à
/// zoom-image, pas de dégressivité dans le temps : les points sont fixes (ou basés sur le rang) tant que
/// la fenêtre de réponse est ouverte. Sert aussi de base à BlindTestEngine (même config, payload différent).
/// </summary>
public class QaEngine : IFeatureEngine
{
    protected static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    protected static readonly JsonSerializerOptions PublicJsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public virtual string FeatureTypeKey => "qa-text";

    public FeatureRuntimeState ComputeState(string configJson, DateTime questionStartedAt, DateTime? pausedAt, DateTime now)
    {
        var config = ParseConfig(configJson);
        var elapsedSeconds = SessionTiming.ComputeElapsedSeconds(questionStartedAt, pausedAt, now);
        return ComputeStateAtElapsed(config, elapsedSeconds);
    }

    public int PointsForElapsedSeconds(string configJson, double elapsedSeconds) =>
        ComputeStateAtElapsed(ParseConfig(configJson), elapsedSeconds).CurrentPoints;

    public virtual FeatureAnswerEvaluation Evaluate(
        string configJson,
        string payloadJson,
        string rawAnswer,
        DateTime questionStartedAt,
        DateTime? pausedAt,
        DateTime submittedAt)
    {
        var config = ParseConfig(configJson);
        var payload = ParsePayload(payloadJson);

        if (config.ValidationMode == "Manual")
        {
            return new FeatureAnswerEvaluation(null, 0);
        }

        var expectedAnswers = payload.ExpectedAnswersOrLegacy();
        var submittedAnswers = ExpectedAnswerMatching.SplitRawAnswer(rawAnswer, expectedAnswers.Count);
        var result = ExpectedAnswerMatching.Match(expectedAnswers, submittedAnswers, e => EffectivePoints(e, config.Points));

        return new FeatureAnswerEvaluation(result.AllMatched, result.PointsAwarded);
    }

    /// <summary>Points qu'une réponse attendue précise rapporte si trouvée : son propre montant si
    /// renseigné (mode "points personnalisés"), sinon le barème uniforme de la manche.</summary>
    protected static int EffectivePoints(ExpectedAnswer expected, int uniformPoints) => expected.Points ?? uniformPoints;

    public bool IsManualValidation(string configJson) => ParseConfig(configJson).ValidationMode == "Manual";

    /// <summary>En validation manuelle, Evaluate() ne calcule jamais rien (aucun matching automatique,
    /// c'est au GM de juger) — sans ce hook, un clic "Correct" du GM sur une soumission à plusieurs champs
    /// n'attribuerait que le barème d'UNE réponse au lieu de la somme des réponses attendues, exactement
    /// comme le fait déjà Evaluate() en mode Auto via ExpectedAnswerMatching.Match/EffectivePoints.</summary>
    public int? FixedManualValidationPoints(string payloadJson, string configJson)
    {
        var config = ParseConfig(configJson);
        if (config.ValidationMode != "Manual")
        {
            return null;
        }

        var expectedAnswers = ParsePayload(payloadJson).ExpectedAnswersOrLegacy();
        return expectedAnswers.Count == 0 ? config.Points : expectedAnswers.Sum(e => EffectivePoints(e, config.Points));
    }

    /// <summary>En mode buzzer, gouverne le droit de re-buzzer après une mauvaise réponse (voir GetRetryCooldownSeconds).</summary>
    public bool AllowsRetryAfterWrongAnswer(string configJson) => ParseConfig(configJson).AllowRetry;

    /// <summary>Délai avant de pouvoir retenter sa chance après une mauvaise réponse — le champ consulté dépend
    /// du mode (buzzer ou réponse écrite classique), chacun ayant son propre réglage.</summary>
    public int GetRetryCooldownSeconds(string configJson)
    {
        var config = ParseConfig(configJson);
        return config.BuzzerMode ? config.BuzzerRetryCooldownSeconds : config.RetryCooldownSeconds;
    }

    public bool IsBuzzerMode(string configJson) => ParseConfig(configJson).BuzzerMode;

    /// <summary>En mode buzzer, une seule bonne réponse suffit : la question est une course, pas un test collectif.</summary>
    public bool RequiresAllPlayersToAnswer(string configJson) => !ParseConfig(configJson).BuzzerMode;

    /// <summary>Sans accès à Round.ConfigJson (contrat IFeatureEngine minimal), le barème uniforme
    /// affiché au joueur retombe sur la valeur par défaut — en pratique le contrôleur appelle toujours
    /// la surcharge à deux paramètres ci-dessous, qui a le vrai Points de la manche.</summary>
    public virtual string BuildPublicPayloadJson(string payloadJson) => BuildPublicPayloadJsonCore(payloadJson, new QaRoundConfig().Points);

    /// <summary>Variante avec Round.ConfigJson : nécessaire pour connaître le barème uniforme (Points) et
    /// calculer expectedAnswerPoints. Partagée par BlindTestEngine/ImageGuessEngine (mêmes questionText +
    /// réponses attendues, seul le champ média diffère).</summary>
    public virtual string BuildPublicPayloadJson(string payloadJson, string configJson) =>
        BuildPublicPayloadJsonCore(payloadJson, ParseConfig(configJson).Points);

    protected string BuildPublicPayloadJsonCore(string payloadJson, int uniformPoints)
    {
        var payload = ParsePayload(payloadJson);
        var (count, points) = BuildExpectedAnswerFields(payload.ExpectedAnswersOrLegacy(), uniformPoints);

        return JsonSerializer.Serialize(
            new { questionText = payload.QuestionText, expectedAnswerCount = count, expectedAnswerPoints = points },
            PublicJsonOptions);
    }

    /// <summary>(nombre de réponses attendues, barème visible du joueur) — réutilisé par
    /// BlindTestEngine/ImageGuessEngine, dont le payload n'a pas de QuestionText et doit donc composer son
    /// propre objet JSON plutôt que de passer par BuildPublicPayloadJsonCore.</summary>
    protected static (int Count, List<int>? Points) BuildExpectedAnswerFields(List<ExpectedAnswer> expectedAnswers, int uniformPoints) => (
        Math.Max(1, expectedAnswers.Count),
        expectedAnswers.Count > 0 ? ExpectedAnswerMatching.BuildPointsArray(expectedAnswers, e => EffectivePoints(e, uniformPoints)) : null
    );

    /// <summary>Pas de palier à révéler progressivement : une fois tout le monde répondu, plus rien à attendre.</summary>
    public double GetFastForwardTargetElapsedSeconds(string configJson) => ParseConfig(configJson).AnswerTimeSeconds;

    public bool UsesRankBasedScoring(string configJson) => ParseConfig(configJson).RankBasedScoring;

    public int PointsForRank(string configJson, int correctAnswerRank)
    {
        var config = ParseConfig(configJson);
        return RankScoring.PointsForRank(config.RankMaxPoints, config.RankPointsDecrement, correctAnswerRank);
    }

    private static FeatureRuntimeState ComputeStateAtElapsed(QaRoundConfig config, double elapsedSeconds)
    {
        var secondsRemaining = (int)Math.Ceiling(Math.Max(0, config.AnswerTimeSeconds - elapsedSeconds));
        var isAnswerWindowOpen = elapsedSeconds < config.AnswerTimeSeconds;
        var shouldAutoAdvance = config.AutoAdvance && !isAnswerWindowOpen;
        // En scoring au rang, le nombre de points affiché ici (avant réponse) est le meilleur cas possible :
        // le rang exact dépend des réponses déjà validées des autres joueurs, connu seulement du contrôleur.
        var currentPoints = config.RankBasedScoring ? config.RankMaxPoints : config.Points;

        // Pas de palier : le temps restant "global" est le même que le temps restant du seul palier.
        return new FeatureRuntimeState(1, currentPoints, secondsRemaining, secondsRemaining, isAnswerWindowOpen, shouldAutoAdvance, elapsedSeconds);
    }

    protected static QaRoundConfig ParseConfig(string configJson) =>
        JsonSerializer.Deserialize<QaRoundConfig>(configJson, JsonOptions) ?? new QaRoundConfig();

    private static QaQuestionPayload ParsePayload(string payloadJson) =>
        JsonSerializer.Deserialize<QaQuestionPayload>(payloadJson, JsonOptions) ?? new QaQuestionPayload();
}
