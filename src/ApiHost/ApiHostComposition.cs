using BuildingBlocks.Application.Dispatching;
using BuildingBlocks.Infrastructure;
using BuildingBlocks.Infrastructure.Dispatching;
using BuildingBlocks.Infrastructure.Http;
using BuildingBlocks.Infrastructure.Modules;
using BuildingBlocks.Infrastructure.OpenApi;
using BuildingBlocks.Infrastructure.Realtime;
using BuildingBlocks.Infrastructure.Telemetry;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Scalar.AspNetCore;

namespace ApiHost;

public static class ApiHostComposition
{
    private const string FrontendCorsPolicyName = "Frontend";

    public const string HostStatusPath = "/_host/status";

    public static void AddBaselineApiHostServices(this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var corsOptions = ReadFrontendCorsOptions(builder.Configuration);

        builder.Services.AddOptions<FrontendCorsOptions>()
            .Bind(builder.Configuration.GetSection(FrontendCorsOptions.SectionName))
            .Validate(
                static options => options.AllowedOrigins.All(static origin => !string.IsNullOrWhiteSpace(origin)),
                $"{FrontendCorsOptions.SectionName}:AllowedOrigins must not contain blank entries.")
            .Validate(
                static options => options.AllowedOrigins.All(static origin => !origin.Contains('*')),
                $"{FrontendCorsOptions.SectionName}:AllowedOrigins must not contain wildcards; explicit origins are required when credentials are enabled.")
            .Validate(
                static options => options.AllowedMethods.Length > 0
                    && options.AllowedMethods.All(static method => !string.IsNullOrWhiteSpace(method) && method != "*"),
                $"{FrontendCorsOptions.SectionName}:AllowedMethods must be a non-empty list of specific HTTP methods; wildcard '*' is not permitted with credentials.")
            .Validate(
                static options => options.AllowedHeaders.Length > 0
                    && options.AllowedHeaders.All(static header => !string.IsNullOrWhiteSpace(header) && header != "*"),
                $"{FrontendCorsOptions.SectionName}:AllowedHeaders must be a non-empty list of specific header names; wildcard '*' is not permitted with credentials.")
            .Validate(
                static options => options.ExposedHeaders.All(static header => !string.IsNullOrWhiteSpace(header) && header != "*"),
                $"{FrontendCorsOptions.SectionName}:ExposedHeaders must be a list of specific header names; wildcard '*' is not permitted with credentials.")
            .Validate(
                static options => options.PreflightMaxAgeSeconds >= 0,
                $"{FrontendCorsOptions.SectionName}:PreflightMaxAgeSeconds must be non-negative.")
            .ValidateOnStart();

        builder.Services.AddDispatcher();
        builder.Services.AddBuildingBlocksInfrastructureDefaults();
        builder.Services.AddHealthChecks()
            .AddCheck<DatabaseReadinessHealthCheck>("database_readiness", tags: ["ready"]);
        builder.Services.AddSingleton<DatabaseReadinessHealthCheck>();
        builder.Services.AddBaselineOpenApi("v1");
        builder.Services.AddApiModulesFromAssemblyReferences(typeof(Program).Assembly);
        builder.Services.AddModuleEndpointBulkheads();
        builder.Services.AddBaselineOpenTelemetry(builder.Configuration, builder.Environment);

        if (corsOptions.AllowedOrigins.Length > 0)
        {
            builder.Services.AddCors(options =>
            {
                options.AddPolicy(FrontendCorsPolicyName, policy =>
                {
                    policy.WithOrigins(corsOptions.AllowedOrigins)
                        .WithMethods(corsOptions.AllowedMethods)
                        .WithHeaders(corsOptions.AllowedHeaders)
                        .AllowCredentials()
                        .SetPreflightMaxAge(TimeSpan.FromSeconds(corsOptions.PreflightMaxAgeSeconds));

                    if (corsOptions.ExposedHeaders.Length > 0)
                    {
                        policy.WithExposedHeaders(corsOptions.ExposedHeaders);
                    }
                });
            });
        }

        builder.Services.Configure<ForwardedHeadersOptions>(static options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        });

        builder.Services.AddHsts(static options =>
        {
            options.MaxAge = TimeSpan.FromDays(365);
            options.IncludeSubDomains = true;
        });

        builder.Services.AddRateLimiter(static options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        });

        builder.WebHost.ConfigureKestrel(static options =>
        {
            options.Limits.MaxRequestBodySize = 1_048_576;
        });
    }

    public static void MapBaselineApiHost(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var corsOptions = app.Services.GetRequiredService<IOptions<FrontendCorsOptions>>().Value;

        app.UseForwardedHeaders();

        if (!app.Environment.IsDevelopment())
        {
            app.UseHsts();
        }

        app.UseHttpsRedirection();
        app.UseSecurityHeaders();

        if (corsOptions.AllowedOrigins.Length > 0)
        {
            app.UseCors(FrontendCorsPolicyName);
        }

        app.UseRequestContext();
        app.UseStaticFiles();
        app.UseExceptionHandler();
        app.UseAuthentication();
        app.UseBrowserMutationAntiforgery();
        app.UseAuthorization();
        app.UseRateLimiter();

        app.MapHealthChecks("/health");
        app.MapHealthChecks("/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains("ready")
        });
        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi("/openapi/{documentName}.json");
            app.MapScalarApiReference(options =>
            {
                options
                    .WithTitle("Baseline API")
                    .WithDefaultHttpClient(ScalarTarget.CSharp, ScalarClient.HttpClient);
            });
        }
        app.MapHub<BrowserRealtimeHub>(BrowserRealtimeDefaults.Path)
            .RequireAuthorization();
        app.MapGet("/", static (HttpContext httpContext, IWebHostEnvironment environment) => MapRootRequest(httpContext, environment))
            .WithName("Host_GetBootstrapStatus")
            .WithTags("Host")
            .WithSummary("Get the bootstrap status for the composed host.")
            .Produces(StatusCodes.Status200OK);
        app.MapGet(HostStatusPath, () => Results.Ok(new { status = "bootstrap" }))
            .ExcludeFromDescription();
        app.MapApiModules();
        app.MapFallbackToFile("index.html")
            .ExcludeFromDescription();
    }

    private static FrontendCorsOptions ReadFrontendCorsOptions(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var options = configuration.GetSection(FrontendCorsOptions.SectionName).Get<FrontendCorsOptions>()
            ?? new FrontendCorsOptions();

        options.AllowedOrigins = options.AllowedOrigins
            .Where(static origin => !string.IsNullOrWhiteSpace(origin))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return options;
    }

    private static IResult MapRootRequest(HttpContext httpContext, IWebHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(environment);

        if (AcceptsHtml(httpContext.Request) && TryGetFrontendShellPath(environment, out var indexPath))
        {
            return Results.File(indexPath, "text/html");
        }

        return Results.Ok(new { status = "bootstrap" });
    }

    private static bool AcceptsHtml(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return request.Headers.Accept.ToString().Contains("text/html", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryGetFrontendShellPath(IWebHostEnvironment environment, out string indexPath)
    {
        ArgumentNullException.ThrowIfNull(environment);

        var fileInfo = environment.WebRootFileProvider.GetFileInfo("index.html");
        if (fileInfo.Exists && !string.IsNullOrWhiteSpace(fileInfo.PhysicalPath))
        {
            indexPath = fileInfo.PhysicalPath;
            return true;
        }

        indexPath = string.Empty;
        return false;
    }
}
