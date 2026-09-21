using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using MochaBot.Config;
using MochaBot.Utils;

namespace MochaBot.Services;

public sealed class MochaApiException : Exception
{
    public HttpStatusCode? Status { get; }
    public MochaApiException(string message, HttpStatusCode? status = null, Exception? inner = null)
        : base(message, inner) => Status = status;
}

public sealed class MochaApiClient
{
    private readonly HttpClient _http;
    private readonly BotConfig _config;

    public MochaApiClient(HttpClient http, BotConfig config)
    {
        _http = http;
        _config = config;
        _http.Timeout = TimeSpan.FromSeconds(10);
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Mocha-Discord-Bot/2.0");
    }

    public string RecNetLink(string fragment = "")
    {
        var suffix = string.IsNullOrEmpty(fragment) ? "" : $"#{fragment.TrimStart('#')}";
        return $"{_config.MochaRecNetUrl}/recnet/{suffix}";
    }

    public string? AssetUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var normalized = value.Replace('\\', '/');
        return Uri.TryCreate(new Uri(_config.MochaApiUrl + "/"), normalized, out var uri)
            ? uri.ToString()
            : null;
    }

    private async Task<JsonElement> RequestAsync(string path, CancellationToken ct = default)
    {
        var url = new Uri(new Uri(_config.MochaApiUrl + "/"), path.TrimStart('/'));
        HttpResponseMessage response;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Accept.ParseAdd("application/json");
            response = await _http.SendAsync(request, ct);
        }
        catch (TaskCanceledException ex)
        {
            throw new MochaApiException("The Mocha server took too long to respond.", null, ex);
        }
        catch (Exception ex)
        {
            throw new MochaApiException($"The Mocha server could not be reached ({url.Host}).", null, ex);
        }

        var raw = await response.Content.ReadAsStringAsync(ct);
        JsonElement body = default;
        var parsed = false;
        if (!string.IsNullOrWhiteSpace(raw))
        {
            try
            {
                body = JsonDocument.Parse(raw).RootElement.Clone();
                parsed = true;
            }
            catch
            {
                if (response.IsSuccessStatusCode)
                    throw new MochaApiException("Mocha returned a non-JSON response. Check MOCHA_API_URL.", response.StatusCode);
            }
        }

        if (!response.IsSuccessStatusCode)
        {
            var message = parsed
                ? body.Str("error") ?? body.Str("message") ?? body.Str("title") ?? $"Mocha returned HTTP {(int)response.StatusCode}."
                : (string.IsNullOrWhiteSpace(raw) ? $"Mocha returned HTTP {(int)response.StatusCode}." : raw.Trim());
            throw new MochaApiException(message.Length > 300 ? message[..300] : message, response.StatusCode);
        }

        return body;
    }

    private static JsonElement AsObject(JsonElement payload)
    {
        if (payload.ValueKind == JsonValueKind.Object)
        {
            if (payload.TryProp("data", out var data) && data.ValueKind == JsonValueKind.Object)
                return data;
            return payload;
        }
        throw new MochaApiException("Mocha returned an unexpected response.");
    }

    private static List<JsonElement> AsList(JsonElement payload)
    {
        JsonElement array = payload;
        if (payload.ValueKind == JsonValueKind.Object)
        {
            foreach (var key in new[] { "data", "results", "items" })
            {
                if (payload.TryProp(key, out var inner) && inner.ValueKind == JsonValueKind.Array)
                {
                    array = inner;
                    break;
                }
            }
        }
        if (array.ValueKind != JsonValueKind.Array)
            throw new MochaApiException("Mocha returned an unexpected list response.");
        return array.EnumerateArray().ToList();
    }

    public async Task<List<JsonElement>> SearchPlayersAsync(string query, CancellationToken ct = default)
    {
        query = query.Trim();
        if (query.Length == 0)
            return new();

        if (query.All(char.IsDigit))
        {
            try
            {
                return new() { AsObject(await RequestAsync($"/recnet/api/users/{Uri.EscapeDataString(query)}", ct)) };
            }
            catch (MochaApiException ex) when (ex.Status == HttpStatusCode.NotFound)
            {
                return new();
            }
        }

        return AsList(await RequestAsync($"/recnet/api/users?search={Uri.EscapeDataString(query)}", ct));
    }

    public async Task<JsonElement?> GetPlayerAsync(string query, CancellationToken ct = default)
    {
        query = query.Trim();
        if (query.Length == 0)
            return null;

        if (query.All(char.IsDigit))
        {
            try
            {
                return AsObject(await RequestAsync($"/recnet/api/users/{Uri.EscapeDataString(query)}", ct));
            }
            catch (MochaApiException ex) when (ex.Status == HttpStatusCode.NotFound)
            {
                return null;
            }
        }

        var players = await SearchPlayersAsync(query, ct);
        var normalized = query.TrimStart('@').ToLowerInvariant();
        var match = players.FirstOrDefault(p => p.Str("username")?.ToLowerInvariant() == normalized)
            .ValueKindOr(players.FirstOrDefault(p => p.Str("displayName")?.ToLowerInvariant() == normalized))
            .ValueKindOr(players.FirstOrDefault());

        if (match.ValueKind != JsonValueKind.Object)
            return null;

        var id = match.Long("accountId");
        try
        {
            return AsObject(await RequestAsync($"/recnet/api/users/{id}", ct));
        }
        catch (MochaApiException ex) when (ex.Status == HttpStatusCode.NotFound)
        {
            return match;
        }
    }

    public async Task<List<JsonElement>> SearchRoomsAsync(string query, CancellationToken ct = default) =>
        AsList(await RequestAsync($"/recnet/api/rooms?search={Uri.EscapeDataString(query.Trim())}", ct));

    public async Task<JsonElement?> GetRoomAsync(string query, CancellationToken ct = default)
    {
        query = query.Trim();
        if (query.Length == 0)
            return null;

        if (query.All(char.IsDigit))
        {
            try
            {
                return AsObject(await RequestAsync($"/recnet/api/rooms/{Uri.EscapeDataString(query)}", ct));
            }
            catch (MochaApiException ex) when (ex.Status == HttpStatusCode.NotFound)
            {
                return null;
            }
        }

        var rooms = await SearchRoomsAsync(query, ct);
        var normalized = query.TrimStart('^').ToLowerInvariant();
        var match = rooms.FirstOrDefault(r => r.Str("name")?.ToLowerInvariant() == normalized);
        if (match.ValueKind != JsonValueKind.Object)
            match = rooms.FirstOrDefault();
        return match.ValueKind == JsonValueKind.Object ? match : (JsonElement?)null;
    }

    public async Task<JsonElement> GetStatusAsync(CancellationToken ct = default) =>
        AsObject(await RequestAsync("/recnet/api/status", ct));

    public async Task<List<JsonElement>> GetEventsAsync(CancellationToken ct = default) =>
        AsList(await RequestAsync("/recnet/api/events", ct));

    public async Task<List<JsonElement>> GetPhotosAsync(int take = 25, CancellationToken ct = default)
    {
        take = Math.Clamp(take, 1, 100);
        return AsList(await RequestAsync($"/recnet/api/photos/newest?take={take}", ct));
    }

    public async Task<JsonElement> AdminActionAsync(string action, object payload, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_config.BotAdminSecret))
            throw new MochaApiException("The bot admin bridge is not configured (BOT_ADMIN_SECRET missing).");

        var url = new Uri(new Uri(_config.MochaApiUrl + "/"), $"recnet/api/bot/{action.TrimStart('/')}");
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(payload),
        };
        request.Headers.Add("X-Bot-Secret", _config.BotAdminSecret);

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, ct);
        }
        catch (Exception ex)
        {
            throw new MochaApiException("Could not reach the Mocha admin bridge.", null, ex);
        }

        var raw = await response.Content.ReadAsStringAsync(ct);
        JsonElement body = default;
        if (!string.IsNullOrWhiteSpace(raw))
        {
            try { body = JsonDocument.Parse(raw).RootElement.Clone(); } catch { }
        }

        if (!response.IsSuccessStatusCode)
        {
            var message = body.ValueKind == JsonValueKind.Object
                ? body.Str("error") ?? body.Str("message") ?? $"Admin bridge returned HTTP {(int)response.StatusCode}."
                : $"Admin bridge returned HTTP {(int)response.StatusCode}.";
            throw new MochaApiException(message, response.StatusCode);
        }

        return body;
    }
}

file static class JsonElementExtensions
{
    public static JsonElement ValueKindOr(this JsonElement first, JsonElement second) =>
        first.ValueKind == JsonValueKind.Object ? first : second;
}
