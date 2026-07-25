using System.Text.Json.Nodes;

namespace Server.Services;

public static class StepConfigSanitizer
{
    /// <summary>Retire les champs "réponse" du config JSON avant envoi aux joueurs.</summary>
    public static string StripAnswer(string configJson)
    {
        var node = JsonNode.Parse(configJson)?.AsObject();
        if (node is null)
        {
            return configJson;
        }

        node.Remove("answer");
        node.Remove("toleranceRatio");

        return node.ToJsonString();
    }
}
