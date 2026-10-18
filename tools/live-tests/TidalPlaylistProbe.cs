#:package System.Security.Cryptography.ProtectedData@9.0.10

using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json.Nodes;

// Account API comparison. Writes require --write and affect only uniquely named playlists created by this run.
if (!OperatingSystem.IsWindows() || args.Length is < 1 or > 2 || args.Length == 2 && args[1] != "--write")
{
    Console.Error.WriteLine("Usage on Windows: dotnet run --file tools/live-tests/TidalPlaylistProbe.cs -- SESSION.bin [--write]");
    return 1;
}

var path = Path.GetFullPath(args[0]);
try
{
    using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(30) };
    var session = JsonNode.Parse(ProtectedData.Unprotect(await File.ReadAllBytesAsync(path), null, DataProtectionScope.CurrentUser))!.AsObject();
    var access = session["AccessToken"]!.GetValue<string>();
    var country = session["Country"]!.GetValue<string>();
    string? userId = session["UserId"]?.ToString();

    if (DateTimeOffset.Parse(session["Expires"]!.GetValue<string>()) <= DateTimeOffset.UtcNow.AddMinutes(5))
    {
        using var response = await http.PostAsync("https://auth.tidal.com/v1/oauth2/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token", ["client_id"] = session["ClientId"]!.GetValue<string>(),
            ["refresh_token"] = session["RefreshToken"]!.GetValue<string>(), ["scope"] = "r_usr+w_usr+w_sub",
        }));
        Console.WriteLine($"Refresh: HTTP {(int)response.StatusCode}");
        if (!response.IsSuccessStatusCode) return 1;
        var refreshed = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        access = refreshed["access_token"]!.GetValue<string>();
        session["AccessToken"] = access;
        if (refreshed["refresh_token"]?.GetValue<string>() is { Length: > 0 } refresh) session["RefreshToken"] = refresh;
        session["Expires"] = DateTimeOffset.UtcNow.AddSeconds(refreshed["expires_in"]!.GetValue<int>()).ToString("O");
        userId ??= refreshed["user_id"]?.ToString() ?? refreshed["user"]?["userId"]?.ToString();
        session["UserId"] = userId;
        // A refresh can rotate the token. Save immediately, and only to the explicitly supplied test session file.
        var encrypted = ProtectedData.Protect(System.Text.Encoding.UTF8.GetBytes(session.ToJsonString()), null, DataProtectionScope.CurrentUser);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(temporary, encrypted);
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        Console.WriteLine("Refreshed test session saved encrypted. No app settings changed.");
        if (refreshed["scope"]?.GetValue<string>() is { } scopes)
        {
            // Only print the known permission names, never arbitrary response strings.
            var known = new[] { "r_usr", "w_usr", "w_sub", "user.read", "playlists.read", "playlists.write", "collection.read" };
            var granted = scopes.Split([' ', '+'], StringSplitOptions.RemoveEmptyEntries);
            Console.WriteLine("Known scopes returned: " + string.Join(", ", known.Where(granted.Contains)));
        }
    }

    async Task<JsonNode?> Read(string label, string url, bool official = false)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
        if (official) request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.api+json"));
        using var response = await http.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        JsonNode? json;
        try { json = JsonNode.Parse(body); } catch (System.Text.Json.JsonException) { json = null; }
        var items = json?["items"] as JsonArray ?? json?["data"] as JsonArray;
        Console.WriteLine($"{label}: HTTP {(int)response.StatusCode}" + (items == null ? "" : $", returned {items.Count} entries"));
        // Don't print bodies, account identities, tokens, playlist names, or server-provided links.
        return response.IsSuccessStatusCode ? json : null;
    }

    var c = Uri.EscapeDataString(country);
    if (string.IsNullOrWhiteSpace(userId))
    {
        var current = await Read("Account session", "https://api.tidal.com/v1/sessions");
        userId = current?["userId"]?.ToString();
    }
    if (!string.IsNullOrWhiteSpace(userId))
        await Read("Unofficial v1 owned playlists", $"https://api.tidal.com/v1/users/{Uri.EscapeDataString(userId)}/playlists?countryCode={c}&limit=10&offset=0");
    else Console.WriteLine("Unofficial v1 owned playlists: skipped, no user ID returned");

    await Read("Unofficial v2 collection", $"https://api.tidal.com/v2/my-collection/playlists/folders?folderId=root&includeOnly=PLAYLIST&countryCode={c}&limit=10");
    await Read("Official v2 account", $"https://openapi.tidal.com/v2/users/me?countryCode={c}", official: true);
    await Read("Official v2 playlist collection", $"https://openapi.tidal.com/v2/userCollectionPlaylists/me/relationships/items?countryCode={c}&include=items", official: true);

    JsonNode? FirstResource(JsonNode? json) => json?["data"] is JsonArray data ? data.FirstOrDefault() : json?["data"];
    foreach (var kind in new[] { "tracks", "albums" })
    {
        var search = await Read($"Official {kind} search", $"https://openapi.tidal.com/v2/searchResults?filter%5Bquery%5D=daft%20punk&include={kind},{kind}.artists,{kind}." + (kind == "tracks" ? "albums.coverArt" : "coverArt") + $"&countryCode={c}", true);
        if (FirstResource(search)?["relationships"]?[kind]?["data"] is not JsonArray { Count: > 0 })
            throw new InvalidOperationException("Search has no results");
    }
    var track = await Read("Official track metadata", $"https://openapi.tidal.com/v2/tracks/20115564?include=artists,albums,albums.coverArt&countryCode={c}", true);
    if (FirstResource(track)?["attributes"]?["isrc"]?.ToString() != "USQX91300108" || track?["included"] is not JsonArray { Count: > 0 })
        throw new InvalidOperationException("Track metadata is incomplete");
    var album = await Read("Official album metadata and items", $"https://openapi.tidal.com/v2/albums/20115556?include=items,items.artists,artists,coverArt&countryCode={c}", true);
    if (FirstResource(album)?["relationships"]?["items"]?["data"] is not JsonArray { Count: > 0 })
        throw new InvalidOperationException("Album has no track references");
    var numbering = await Read("Official album track positions", $"https://openapi.tidal.com/v2/albums/20115556/relationships/items?countryCode={c}", true);
    if (numbering?["data"] is not JsonArray { Count: > 0 } numbered || !numbered.Any(item => item?["meta"]?["trackNumber"] != null))
        throw new InvalidOperationException("Album positions are missing");
    var isrc = await Read("Official ISRC lookup", $"https://openapi.tidal.com/v2/tracks?filter%5Bisrc%5D=USQX91300108&include=artists,albums&countryCode={c}", true);
    if (isrc?["data"] is not JsonArray { Count: > 0 }) throw new InvalidOperationException("ISRC lookup is empty");
    Console.WriteLine("Catalog checks passed: track/album searches, metadata, album items and positions, ISRC lookup.");
    if (args.Length == 1)
    {
        Console.WriteLine("Read-only probe complete. No playlists changed.");
        return 0;
    }

    async Task<JsonNode?> Mutate(string label, HttpMethod method, string url, HttpContent? content = null, bool official = false, string? etag = null)
    {
        using var request = new HttpRequestMessage(method, url) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
        if (etag != null) request.Headers.TryAddWithoutValidation("If-None-Match", etag);
        if (official)
        {
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.api+json"));
            request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        }
        using var response = await http.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        Console.WriteLine($"{label}: HTTP {(int)response.StatusCode}");
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"{label} failed");
        return string.IsNullOrWhiteSpace(body) ? null : JsonNode.Parse(body);
    }

    HttpContent JsonContent(JsonNode node) => new StringContent(node.ToJsonString(), System.Text.Encoding.UTF8, "application/vnd.api+json");
    var expected = new[] { "20115564", "293301151", "20115564" };
    foreach (var official in new[] { false, true })
    {
        var kind = official ? "Official" : "Unofficial";
        var name = "FluentDL playlist probe " + Guid.NewGuid().ToString("N");
        string? createdId = null;
        try
        {
            JsonNode? created;
            if (official)
            {
                var payload = new JsonObject { ["data"] = new JsonObject { ["type"] = "playlists", ["attributes"] = new JsonObject
                    { ["name"] = name, ["description"] = "Disposable FluentDL API test", ["accessType"] = "UNLISTED" } } };
                created = await Mutate(kind + " create", HttpMethod.Post, "https://openapi.tidal.com/v2/playlists", JsonContent(payload), true);
                createdId = created?["data"]?["id"]?.ToString();
            }
            else
            {
                created = await Mutate(kind + " create", HttpMethod.Put,
                    $"https://api.tidal.com/v2/my-collection/playlists/folders/create-playlist?name={Uri.EscapeDataString(name)}&description=Disposable%20FluentDL%20API%20test&folderId=root&countryCode={c}");
                createdId = created?["data"]?["uuid"]?.ToString();
            }
            if (!Guid.TryParse(createdId, out _)) throw new InvalidOperationException("Create response has no playlist UUID");
            Console.WriteLine($"{kind} disposable playlist ID: {createdId}");
            if (official)
            {
                var metadata = await Read(kind + " visibility", $"https://openapi.tidal.com/v2/playlists/{createdId}?countryCode={c}", true);
                var resource = metadata?["data"] is JsonArray resources ? resources.FirstOrDefault() : metadata?["data"];
                if (resource?["attributes"]?["accessType"]?.ToString() != "UNLISTED") throw new InvalidOperationException("Unlisted visibility was not verified");
                Console.WriteLine("Official unlisted visibility verified.");
            }
            if (official)
            {
                var payload = new JsonObject { ["data"] = new JsonArray(expected.Select(id => (JsonNode)new JsonObject
                    { ["type"] = "tracks", ["id"] = id }).ToArray()), ["meta"] = new JsonObject { ["onDuplicates"] = "ADD" } };
                await Mutate(kind + " append", HttpMethod.Post, $"https://openapi.tidal.com/v2/playlists/{createdId}/relationships/items?countryCode={c}", JsonContent(payload), true);
            }
            else
            {
                using var versionRequest = new HttpRequestMessage(HttpMethod.Get, $"https://api.tidal.com/v1/playlists/{createdId}?countryCode={c}");
                versionRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
                using var version = await http.SendAsync(versionRequest);
                if (!version.IsSuccessStatusCode || version.Headers.ETag == null) throw new InvalidOperationException("Could not read the playlist ETag");
                await Mutate(kind + " append", HttpMethod.Post, $"https://api.tidal.com/v1/playlists/{createdId}/items?countryCode={c}",
                    new FormUrlEncodedContent(new Dictionary<string, string> { ["trackIds"] = string.Join(",", expected), ["onDupes"] = "ADD", ["onArtifactNotFound"] = "FAIL", ["toIndex"] = "0" }), etag: version.Headers.ETag.ToString());
            }
            var actual = await Read(kind + " read back", official
                ? $"https://openapi.tidal.com/v2/playlists/{createdId}/relationships/items?countryCode={c}&sort=itemIndex"
                : $"https://api.tidal.com/v1/playlists/{createdId}/items?countryCode={c}&limit=10&offset=0", official);
            var ids = official
                ? (actual?["data"] as JsonArray)?.Select(item => item?["id"]?.ToString()).ToArray() ?? []
                : (actual?["items"] as JsonArray)?.Select(item => item?["item"]?["id"]?.ToString()).ToArray() ?? [];
            if (!expected.SequenceEqual(ids)) throw new InvalidOperationException("Read-back differs from the requested order");
            Console.WriteLine(kind + " read-back verified: 3 tracks, original order and duplicate retained.");
        }
        finally
        {
            if (Guid.TryParse(createdId, out _))
            {
                // Only delete the UUID returned by this run's create response. Never take a deletion target from the user.
                var cleanupUrl = official
                    ? $"https://openapi.tidal.com/v2/playlists/{createdId}?countryCode={c}"
                    : $"https://api.tidal.com/v1/playlists/{createdId}?countryCode={c}";
                await Mutate(kind + " cleanup", HttpMethod.Delete, cleanupUrl, official: official);
                using var verify = new HttpRequestMessage(HttpMethod.Get, cleanupUrl);
                verify.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
                if (official) verify.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.api+json"));
                using var removed = await http.SendAsync(verify);
                Console.WriteLine($"{kind} cleanup verification: HTTP {(int)removed.StatusCode}");
                if (removed.StatusCode != HttpStatusCode.NotFound) throw new InvalidOperationException("Deletion was not verified");
            }
        }
    }
    Console.WriteLine("Write probe complete. Both disposable playlists deleted and deletion verified.");
    return 0;
}
catch (Exception error)
{
    // Exception messages can carry a URL or response data. Print only the type.
    Console.Error.WriteLine($"Probe stopped: {error.GetType().Name}. No response bodies or credentials logged.");
    return 1;
}
