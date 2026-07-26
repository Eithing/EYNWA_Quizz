namespace QuizParty.Api.Features.Shared;

/// <summary>Registre des moteurs d'exécution de feature, peuplé via DI — ajouter une feature ne touche pas ce fichier.</summary>
public class FeatureEngineRegistry
{
    private readonly Dictionary<string, IFeatureEngine> _byKey;

    public FeatureEngineRegistry(IEnumerable<IFeatureEngine> engines)
    {
        _byKey = engines.ToDictionary(e => e.FeatureTypeKey);
    }

    public IFeatureEngine Get(string featureTypeKey) =>
        _byKey.TryGetValue(featureTypeKey, out var engine)
            ? engine
            : throw new InvalidOperationException($"Aucun moteur enregistré pour la feature '{featureTypeKey}'.");
}
