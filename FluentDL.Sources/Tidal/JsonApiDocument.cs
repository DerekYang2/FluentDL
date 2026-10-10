using System.Text.Json;

namespace FluentDL.Services.Tidal;

/// <summary>
/// A TIDAL API response. TIDAL uses JSON:API: "data" holds the requested resources (each with a type, id,
/// attributes and relationships), and "included" holds the related resources asked for with include=.
/// A relationship only lists references (type and id), in the API's order; the resources themselves are in "included".
/// </summary>
internal sealed class JsonApiDocument
{
    private readonly Dictionary<(string Type, string Id), JsonElement> included = [];

    public JsonApiDocument(JsonElement root)
    {
        Root = root;
        if (root.TryGetProperty("included", out var resources) && resources.ValueKind == JsonValueKind.Array)
        {
            foreach (var resource in resources.EnumerateArray())
            {
                included[(TypeOf(resource), IdOf(resource))] = resource;
            }
        }
    }

    public JsonElement Root { get; }

    /// <summary>The resources in "data", as a list whether it holds one resource or several.</summary>
    public IReadOnlyList<JsonElement> Data =>
        !Root.TryGetProperty("data", out var data) ? []
        : data.ValueKind == JsonValueKind.Array ? data.EnumerateArray().ToList()
        : data.ValueKind == JsonValueKind.Object ? [data]
        : [];

    /// <summary>The link to the next page of the top-level "data", or null on the last page.</summary>
    public string? NextLink => NextLinkOf(Root);

    /// <summary>The included resource a reference points to, or null when the response didn't include it.</summary>
    public JsonElement? Find(JsonElement reference) =>
        included.TryGetValue((TypeOf(reference), IdOf(reference)), out var resource) ? resource : null;

    /// <summary>
    /// The included resources a relationship points to, in the relationship's order (for artists, the credit order;
    /// for search results, the ranking). References the response didn't include are skipped.
    /// </summary>
    public List<JsonElement> Related(JsonElement resource, string relationship) =>
        References(resource, relationship).Select(Find).OfType<JsonElement>().ToList();

    /// <summary>A relationship's references (type, id, and any meta such as a track number), in order.</summary>
    public static IReadOnlyList<JsonElement> References(JsonElement resource, string relationship)
    {
        if (!resource.TryGetProperty("relationships", out var relationships)
            || !relationships.TryGetProperty(relationship, out var related)
            || !related.TryGetProperty("data", out var data))
        {
            return [];
        }
        return data.ValueKind switch
        {
            JsonValueKind.Array => data.EnumerateArray().ToList(),
            JsonValueKind.Object => [data],
            _ => [],
        };
    }

    /// <summary>The link to a relationship's next page, or null on its last page.</summary>
    public static string? RelationshipNextLink(JsonElement resource, string relationship) =>
        resource.TryGetProperty("relationships", out var relationships) && relationships.TryGetProperty(relationship, out var related)
            ? NextLinkOf(related)
            : null;

    public static string IdOf(JsonElement resource) => resource.GetProperty("id").GetString() ?? "";

    public static string TypeOf(JsonElement resource) => resource.GetProperty("type").GetString() ?? "";

    /// <summary>An attribute as text: strings as they are, numbers and booleans in their JSON form, null when absent.</summary>
    public static string? Attribute(JsonElement resource, string name)
    {
        if (!resource.TryGetProperty("attributes", out var attributes) || !attributes.TryGetProperty(name, out var value))
        {
            return null;
        }
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            _ => value.GetRawText(),
        };
    }

    public static JsonElement? AttributeElement(JsonElement resource, string name) =>
        resource.TryGetProperty("attributes", out var attributes) && attributes.TryGetProperty(name, out var value) ? value : null;

    private static string? NextLinkOf(JsonElement owner) =>
        owner.TryGetProperty("links", out var links) && links.TryGetProperty("next", out var next) && next.ValueKind == JsonValueKind.String
            ? next.GetString()
            : null;
}
