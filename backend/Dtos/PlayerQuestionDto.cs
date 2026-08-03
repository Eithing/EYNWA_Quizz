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
    int SecondsRemainingTotal,
    bool IsAnswerWindowOpen,
    bool HasAnswered,
    List<string> CorrectFinders,
    bool IsSpectator,
    bool IsBuzzerMode,
    /// <summary>Position de lecture autoritaire pour l'audio (blind-test) — ignoré par les autres features.</summary>
    double SecondsElapsedTotal,
    /// <summary>Minuteur gelé (revue GM en cours ou pause explicite) : l'audio doit se figer pour tout le monde.</summary>
    bool IsPaused);
