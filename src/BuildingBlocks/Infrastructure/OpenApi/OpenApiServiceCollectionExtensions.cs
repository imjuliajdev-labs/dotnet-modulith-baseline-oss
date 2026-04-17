using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;
using System.Text.Json.Nodes;

namespace BuildingBlocks.Infrastructure.OpenApi;

public static class OpenApiServiceCollectionExtensions
{
    public const string VersionSetExtensionName = "x-baseline-version-set";

    public static IServiceCollection AddBaselineOpenApi(this IServiceCollection services, string documentName)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(documentName);

        services.AddOpenApi(documentName, options =>
        {
            options.AddDocumentTransformer((document, _, _) =>
            {
                ArgumentNullException.ThrowIfNull(document);

                document.Extensions ??= new Dictionary<string, IOpenApiExtension>(StringComparer.Ordinal);
                document.Extensions[VersionSetExtensionName] = new JsonNodeExtension(JsonValue.Create(documentName)!);
                document.Servers = [new OpenApiServer { Url = "/" }];

                if (document.Paths is null)
                {
                    return Task.CompletedTask;
                }

                foreach (var pathItem in document.Paths.Values)
                {
                    if (pathItem?.Operations is null)
                    {
                        continue;
                    }

                    foreach (var operation in pathItem.Operations.Values)
                    {
                        if (operation is null)
                        {
                            continue;
                        }

                        operation.Extensions ??= new Dictionary<string, IOpenApiExtension>(StringComparer.Ordinal);
                        operation.Extensions[VersionSetExtensionName] = new JsonNodeExtension(JsonValue.Create(documentName)!);
                        SortResponses(operation);
                    }
                }

                return Task.CompletedTask;
            });
        });

        return services;
    }

    private static void SortResponses(OpenApiOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        var responses = operation.Responses;
        if (responses is null || responses.Count <= 1)
        {
            return;
        }

        var orderedResponses = responses
            .OrderBy(static entry => GetResponseSortKey(entry.Key))
            .ThenBy(static entry => entry.Key, StringComparer.Ordinal)
            .ToArray();

        responses.Clear();

        foreach (var (statusCode, response) in orderedResponses)
        {
            responses.Add(statusCode, response);
        }
    }

    private static int GetResponseSortKey(string statusCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(statusCode);

        return int.TryParse(statusCode, out var parsedStatusCode)
            ? parsedStatusCode
            : int.MaxValue;
    }
}
