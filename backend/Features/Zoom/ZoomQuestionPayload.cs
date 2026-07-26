namespace QuizParty.Api.Features.Zoom;

public class ZoomFocusPoint
{
    public double X { get; set; }
    public double Y { get; set; }
}

/// <summary>Désérialisé depuis Question.PayloadJson pour une manche "zoom-image".</summary>
public class ZoomQuestionPayload
{
    public string ImageUrl { get; set; } = "";
    public List<string> AcceptedAnswers { get; set; } = [];
    public ZoomFocusPoint ZoomFocusPoint { get; set; } = new();
}
