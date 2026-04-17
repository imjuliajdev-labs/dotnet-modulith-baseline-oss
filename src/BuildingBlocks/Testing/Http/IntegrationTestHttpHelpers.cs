using System.Net.Http.Json;
using System.Text.Json;

namespace BuildingBlocks.Testing.Http;

public sealed record AntiforgeryContext(string HeaderName, string RequestToken, string Cookie, string? SetCookieHeader = null);

public sealed record AuthenticatedSession(string HeaderName, string RequestToken, string Cookie, string Cookies);

public static class IntegrationTestHttpHelpers
{
    public static async Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        HttpMethod method,
        string path,
        string? cookies = null,
        string? antiforgeryHeaderName = null,
        string? antiforgeryToken = null)
    {
        using var request = new HttpRequestMessage(method, path);

        if (!string.IsNullOrWhiteSpace(cookies))
        {
            request.Headers.Add("Cookie", cookies);
        }

        if (!string.IsNullOrWhiteSpace(antiforgeryHeaderName) && !string.IsNullOrWhiteSpace(antiforgeryToken))
        {
            request.Headers.Add(antiforgeryHeaderName, antiforgeryToken);
        }

        return await client.SendAsync(request);
    }

    public static async Task<HttpResponseMessage> SendJsonAsync<TBody>(
        HttpClient client,
        HttpMethod method,
        string path,
        TBody body,
        string? cookies = null,
        string? antiforgeryHeaderName = null,
        string? antiforgeryToken = null,
        string? idempotencyKey = null)
    {
        using var request = new HttpRequestMessage(method, path)
        {
            Content = JsonContent.Create(body),
        };

        if (!string.IsNullOrWhiteSpace(cookies))
        {
            request.Headers.Add("Cookie", cookies);
        }

        if (!string.IsNullOrWhiteSpace(antiforgeryHeaderName) && !string.IsNullOrWhiteSpace(antiforgeryToken))
        {
            request.Headers.Add(antiforgeryHeaderName, antiforgeryToken);
        }

        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        }

        return await client.SendAsync(request);
    }

    public static async Task<AntiforgeryContext> GetAntiforgeryAsync(HttpClient client, string? cookies = null)
    {
        var response = await SendAsync(client, HttpMethod.Get, "/api/v1/identity/antiforgery", cookies);
        response.EnsureSuccessStatusCode();

        using var json = await ReadJsonAsync(response);

        string? setCookieHeader = null;
        if (response.Headers.TryGetValues("Set-Cookie", out var setCookieValues))
        {
            setCookieHeader = string.Join("; ", setCookieValues);
        }

        return new AntiforgeryContext(
            json.RootElement.GetProperty("headerName").GetString() ?? string.Empty,
            json.RootElement.GetProperty("requestToken").GetString() ?? string.Empty,
            GetCookie(response, ".AspNetCore.Antiforgery"),
            setCookieHeader);
    }

    public static async Task<AuthenticatedSession> SignInAsync(
        HttpClient client,
        string userName = "admin",
        string password = "LocalOnly!123")
    {
        var antiforgery = await GetAntiforgeryAsync(client);
        var login = await SendJsonAsync(
            client,
            HttpMethod.Post,
            "/api/v1/identity/session/login",
            new { userName, password },
            antiforgery.Cookie,
            antiforgery.HeaderName,
            antiforgery.RequestToken);

        login.EnsureSuccessStatusCode();
        var authCookie = GetCookie(login, "__Host-dotnet-modulith-baseline");

        var authenticatedAntiforgery = await GetAntiforgeryAsync(client, authCookie);
        return new AuthenticatedSession(
            authenticatedAntiforgery.HeaderName,
            authenticatedAntiforgery.RequestToken,
            authCookie,
            CombineCookies(authCookie, authenticatedAntiforgery.Cookie));
    }

    public static string CombineCookies(params string?[] cookies)
    {
        return string.Join(
            "; ",
            cookies.Where(static cookie => !string.IsNullOrWhiteSpace(cookie)).Select(static cookie => cookie!));
    }

    public static string GetCookie(HttpResponseMessage response, string cookieNamePrefix)
    {
        var setCookie = response.Headers.GetValues("Set-Cookie")
            .Single(header => header.StartsWith(cookieNamePrefix, StringComparison.OrdinalIgnoreCase));

        return setCookie.Split(';', 2)[0];
    }

    public static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        await using var stream = await response.Content.ReadAsStreamAsync();
        return await JsonDocument.ParseAsync(stream);
    }
}
