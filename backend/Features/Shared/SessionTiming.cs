namespace QuizParty.Api.Features.Shared;

/// <summary>Calcul du temps écoulé sur une question, en tenant compte d'une pause éventuelle — générique à toutes les features, le serveur reste seul autoritaire sur le temps.</summary>
public static class SessionTiming
{
    public static double ComputeElapsedSeconds(DateTime questionStartedAt, DateTime? pausedAt, DateTime now)
    {
        var effectiveNow = pausedAt ?? now;
        return Math.Max(0, (effectiveNow - questionStartedAt).TotalSeconds);
    }
}
