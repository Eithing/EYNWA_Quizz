namespace QuizParty.Api.Dtos;

/// <summary>Vue joueur d'une question, quelle que soit la feature : PublicPayloadJson est assaini par le moteur
/// de la feature concernée (jamais les réponses acceptées ni tout autre champ réservé au GM).</summary>
public record PlayerQuestionDto(
    int QuestionId,
    string RoundTitle,
    string FeatureTypeKey,
    string PublicPayloadJson,
    double CurrentLevel,
    int SecondsRemainingInStep,
    bool IsAnswerWindowOpen,
    bool HasAnswered,
    List<string> CorrectFinders,
    bool IsSpectator,
    bool IsBuzzerMode);
