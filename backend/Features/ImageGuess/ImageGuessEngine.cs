using System.Text.Json;
using QuizParty.Api.Features.Qa;
using QuizParty.Api.Features.Shared;

namespace QuizParty.Api.Features.ImageGuess;

/// <summary>
/// Moteur d'exécution de la feature "image-guess" : même principe que "qa-text" (même configuration de
/// manche — validation, buzzer, retry, scoring au rang…), seul le contenu de la question change (une
/// image fixe à deviner plutôt qu'une question écrite). Contrairement à "zoom-image", l'image est
/// affichée intégralement dès le départ, pas de révélation progressive. Hérite de QaEngine pour ne pas
/// dupliquer toute la mécanique de timing/validation/scoring, ne redéfinit que ce qui dépend du payload.
/// </summary>
public class ImageGuessEngine : QaEngine
{
    public override string FeatureTypeKey => "image-guess";

    public override FeatureAnswerEvaluation Evaluate(
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

    public override string BuildPublicPayloadJson(string payloadJson) => BuildPublicPayloadJson(payloadJson, "{}");

    public override string BuildPublicPayloadJson(string payloadJson, string configJson)
    {
        var payload = ParsePayload(payloadJson);
        var (count, points) = BuildExpectedAnswerFields(payload.ExpectedAnswersOrLegacy(), ParseConfig(configJson).Points);

        return JsonSerializer.Serialize(
            new { imageUrl = payload.ImageUrl, expectedAnswerCount = count, expectedAnswerPoints = points, comment = payload.Comment },
            PublicJsonOptions);
    }

    private static ImageGuessQuestionPayload ParsePayload(string payloadJson) =>
        JsonSerializer.Deserialize<ImageGuessQuestionPayload>(payloadJson, JsonOptions) ?? new ImageGuessQuestionPayload();
}
