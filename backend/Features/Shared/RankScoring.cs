namespace QuizParty.Api.Features.Shared;

/// <summary>Formule de points partagée pour toute feature utilisant un scoring dégressif au rang
/// (ordre d'arrivée pour qa-text/zoom-image, rang de proximité pour closest-guess…).</summary>
public static class RankScoring
{
    public static int PointsForRank(int rankMaxPoints, int rankPointsDecrement, int rank) =>
        Math.Max(0, rankMaxPoints - rankPointsDecrement * rank);
}
