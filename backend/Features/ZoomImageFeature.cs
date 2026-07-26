namespace QuizParty.Api.Features;

/// <summary>Feature "Zoom Progressif" (section 6). Moteur d'exécution implémenté en Phase 2.</summary>
public class ZoomImageFeature : IQuizFeature
{
    public string TypeKey => "zoom-image";
    public string DisplayName => "Zoom Progressif";
    public string Description => "Une image se dézoome progressivement ; les joueurs répondent dès qu'ils reconnaissent le sujet.";
}
