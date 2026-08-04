using QuizParty.Api.Features.Qa;

namespace QuizParty.Api.Features.PartnerGuess;

/// <summary>
/// Moteur d'exécution de la feature "partner-guess" ("à quoi pense l'autre") : réutilise QaEngine tel
/// quel (buzzer, validation manuelle/auto, retry…). La seule différence est en amont, côté
/// SessionsController : les "réponses acceptées" ne sont jamais pré-écrites dans l'éditeur — elles sont
/// composées à la volée à partir de la réponse privée du joueur désigné (GameSession.CurrentAnswererPlayerId)
/// avant d'appeler Evaluate()/BuildPublicPayloadJson() (voir ResolvePartnerGuessPayloadJsonAsync).
/// </summary>
public class PartnerGuessEngine : QaEngine
{
    public override string FeatureTypeKey => "partner-guess";
}
