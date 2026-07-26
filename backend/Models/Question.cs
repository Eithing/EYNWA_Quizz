namespace QuizParty.Api.Models;

public class Question
{
    public int Id { get; set; }
    public int RoundId { get; set; }
    public Round? Round { get; set; }

    public int Order { get; set; }

    /// <summary>Structure libre dépendant du FeatureTypeKey de la manche parente.</summary>
    public required string PayloadJson { get; set; }
}
