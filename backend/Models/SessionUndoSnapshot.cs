namespace QuizParty.Api.Models;

/// <summary>Instantané complet de l'état "en jeu" d'une session (hors joueurs/équipes/historique jokers —
/// voir SessionsController.Undo.cs), pris juste avant une action GM à risque de fausse manip (Next,
/// ChooseTheme, LaunchTheme, SkipTheme). Une seule ligne par session : un seul niveau d'annulation, la
/// ligne est remplacée à chaque nouvelle action annulable et supprimée une fois consommée par /undo.</summary>
public class SessionUndoSnapshot
{
    public int Id { get; set; }

    public int SessionId { get; set; }
    public GameSession? Session { get; set; }

    public required string SnapshotJson { get; set; }
    public DateTime CreatedAt { get; set; }
}
