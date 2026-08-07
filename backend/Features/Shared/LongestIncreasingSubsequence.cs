namespace QuizParty.Api.Features.Shared;

/// <summary>Plus longue sous-suite strictement croissante (LIS), utilisée pour noter un classement
/// soumis par rapport à l'ordre correct (feature order-list) : les items de la chaîne sont déjà
/// dans le bon ordre relatif les uns par rapport aux autres, même mal positionnés dans l'absolu
/// (ex : correct 1-2-3-4-5-6, soumis 1-3-4-5-6-2 → chaîne {1,3,4,5,6}, seul le "2" est hors-chaîne,
/// plutôt qu'une comparaison position par position qui ne compterait que le "1" comme correct).</summary>
public static class LongestIncreasingSubsequence
{
    /// <summary>Indices (dans <paramref name="values"/>) qui appartiennent à la plus longue sous-suite
    /// strictement croissante. O(n log n), n toujours petit dans ce contexte (nombre d'items d'une
    /// question à réordonner).</summary>
    public static List<int> ComputeChainIndices(IReadOnlyList<int> values)
    {
        if (values.Count == 0)
        {
            return [];
        }

        // tails[k] = index (dans values) de la plus petite valeur terminant une chaîne de longueur k+1.
        var tails = new List<int>();
        var predecessors = new int[values.Count];

        for (var i = 0; i < values.Count; i++)
        {
            var value = values[i];

            // Recherche binaire : première position de tails dont la valeur pointée est >= value.
            var lo = 0;
            var hi = tails.Count;
            while (lo < hi)
            {
                var mid = (lo + hi) / 2;
                if (values[tails[mid]] < value)
                {
                    lo = mid + 1;
                }
                else
                {
                    hi = mid;
                }
            }

            predecessors[i] = lo > 0 ? tails[lo - 1] : -1;

            if (lo == tails.Count)
            {
                tails.Add(i);
            }
            else
            {
                tails[lo] = i;
            }
        }

        var chain = new List<int>();
        var current = tails[^1];
        while (current != -1)
        {
            chain.Add(current);
            current = predecessors[current];
        }
        chain.Reverse();

        return chain;
    }
}
