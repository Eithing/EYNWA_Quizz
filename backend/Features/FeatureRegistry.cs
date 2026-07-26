namespace QuizParty.Api.Features;

/// <summary>Registre des features de quiz disponibles, peuplé via DI — ajouter une feature ne touche pas ce fichier.</summary>
public class FeatureRegistry
{
    private readonly Dictionary<string, IQuizFeature> _byKey;

    public FeatureRegistry(IEnumerable<IQuizFeature> features)
    {
        _byKey = features.ToDictionary(f => f.TypeKey);
    }

    public IReadOnlyCollection<IQuizFeature> All => _byKey.Values;

    public bool Exists(string typeKey) => _byKey.ContainsKey(typeKey);
}
