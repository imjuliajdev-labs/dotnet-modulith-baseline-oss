using Microsoft.AspNetCore.Http;

namespace ApiHost;

/// <summary>
/// Cross-origin resource sharing policy for the frontend shell. Origins, methods and headers
/// are all explicit allow-lists — wildcards are rejected by <see cref="FrontendCorsOptionsValidator"/>
/// because the policy runs with <c>AllowCredentials</c>.
/// </summary>
public sealed class FrontendCorsOptions
{
    public const string SectionName = "Frontend";

    public const int DefaultPreflightMaxAgeSeconds = 600;

    public static readonly string[] DefaultAllowedMethods =
    [
        HttpMethods.Get,
        HttpMethods.Post,
        HttpMethods.Put,
        HttpMethods.Patch,
        HttpMethods.Delete,
    ];

    public static readonly string[] DefaultAllowedHeaders =
    [
        "Accept",
        "Content-Type",
        "X-CSRF-TOKEN",
        "X-Requested-With",
        "X-SignalR-User-Agent",
    ];

    public string[] AllowedOrigins { get; set; } = [];

    public string[] AllowedMethods { get; set; } = DefaultAllowedMethods;

    public string[] AllowedHeaders { get; set; } = DefaultAllowedHeaders;

    public string[] ExposedHeaders { get; set; } = [];

    public int PreflightMaxAgeSeconds { get; set; } = DefaultPreflightMaxAgeSeconds;
}
