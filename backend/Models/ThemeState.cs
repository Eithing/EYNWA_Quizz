namespace QuizParty.Api.Models;

public enum ThemeResolution
{
    Pending,
    Played,
    Skipped
}

/// <summary>État en direct (par session) d'une sous-manche d'une manche à thèmes : révélée aux joueurs ou
/// non, jouée/skippée ou encore en attente. Créé pour chaque sous-manche à l'entrée dans la manche à
/// thèmes parente, jamais réutilisé d'une session à l'autre.</summary>
public class ThemeState
{
    public int Id { get; set; }

    public int SessionId { get; set; }
    public GameSession? Session { get; set; }

    public int SubRoundId { get; set; }
    public Round? SubRound { get; set; }

    public bool IsRevealed { get; set; }
    public ThemeResolution Resolution { get; set; } = ThemeResolution.Pending;
}
