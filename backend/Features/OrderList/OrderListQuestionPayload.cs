namespace QuizParty.Api.Features.OrderList;

public class OrderListItem
{
    /// <summary>Identifiant stable de l'item (pas son index — l'ordre bouge). Généré côté éditeur.</summary>
    public string Id { get; set; } = "";

    /// <summary>Texte, ou URL média (image/son) selon ContentType de la question.</summary>
    public string Content { get; set; } = "";
}

public class OrderListQuestionPayload
{
    public string QuestionText { get; set; } = "";

    /// <summary>"Text", "Image" ou "Audio" — propre à CETTE question (pas à la manche : une même manche
    /// peut mélanger une question texte, une question image, une question son...).</summary>
    public string ContentType { get; set; } = "Text";

    /// <summary>Ordre de la liste = ordre CORRECT, défini par le GM à l'édition. L'ordre affiché aux
    /// joueurs en jeu vient d'ailleurs (brouillon persisté côté serveur, voir SessionsController) —
    /// jamais de cet ordre-ci tel quel, qui doit rester secret jusqu'à la révélation.</summary>
    public List<OrderListItem> Items { get; set; } = [];
}
