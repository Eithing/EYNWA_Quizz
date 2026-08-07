namespace QuizParty.Api.Features.OrderList;

/// <summary>Désérialisé depuis Round.ConfigJson pour une manche "order-list".</summary>
public class OrderListRoundConfig
{
    public int AnswerTimeSeconds { get; set; } = 60;

    /// <summary>Points par item appartenant à la plus longue chaîne déjà dans le bon ordre relatif
    /// (voir LongestIncreasingSubsequence) — pas de points fixes "tout ou rien".</summary>
    public int PointsPerChainedItem { get; set; } = 20;
}
