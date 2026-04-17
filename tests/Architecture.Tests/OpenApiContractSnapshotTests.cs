using BuildingBlocks.Application.Modules;
using BuildingBlocks.Infrastructure.Modules;
using System.Text.Json;

namespace Architecture.Tests;

public sealed class OpenApiContractSnapshotTests
{
    [Fact]
    public void OpenApiSnapshotDeclaresVersionedDocumentIdentity()
    {
        using var document = RepositoryFiles.ReadJsonDocument("contracts", "http", "openapi.v1.json");
        Assert.Equal("v1", document.RootElement.GetProperty("x-baseline-version-set").GetString());
        var info = document.RootElement.GetProperty("info");

        Assert.Equal("1.0.0", info.GetProperty("version").GetString());
        Assert.Contains("| v1", info.GetProperty("title").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void OpenApiSnapshotOperationsDeclareStableMetadata()
    {
        using var document = RepositoryFiles.ReadJsonDocument("contracts", "http", "openapi.v1.json");
        var operations = EnumerateOperations(document.RootElement.GetProperty("paths")).ToArray();

        Assert.NotEmpty(operations);

        foreach (var operation in operations)
        {
            Assert.False(string.IsNullOrWhiteSpace(operation.OperationId));
            Assert.False(string.IsNullOrWhiteSpace(operation.Summary));
            Assert.Equal("v1", operation.VersionSet);
        }
    }

    [Fact]
    public void OpenApiSnapshotNonSuccessResponsesUseCanonicalProblemDetails()
    {
        using var document = RepositoryFiles.ReadJsonDocument("contracts", "http", "openapi.v1.json");
        var operations = EnumerateOperations(document.RootElement.GetProperty("paths"));

        foreach (var operation in operations)
        {
            foreach (var response in operation.Responses.EnumerateObject())
            {
                if (int.TryParse(response.Name, out var statusCode) && statusCode >= 200 && statusCode < 400)
                {
                    continue;
                }

                var schemaRef = response.Value
                    .GetProperty("content")
                    .GetProperty("application/problem+json")
                    .GetProperty("schema")
                    .GetProperty("$ref")
                    .GetString();

                Assert.Equal("#/components/schemas/ProblemDetails", schemaRef);
            }
        }
    }

    [Fact]
    public void OpenApiSnapshotRejectsPermissionEraIdentityAndBootstrapSurfaces()
    {
        using var document = RepositoryFiles.ReadJsonDocument("contracts", "http", "openapi.v1.json");
        var root = document.RootElement;
        var paths = root.GetProperty("paths");
        var schemas = root.GetProperty("components").GetProperty("schemas");

        Assert.False(
            paths.TryGetProperty("/api/v1/identity/users/{actorId}/permissions", out _),
            "The checked-in OpenAPI snapshot must not reintroduce the removed identity permissions endpoint.");

        var identitySessionResponse = schemas.GetProperty("IdentityActorSessionResponse").GetProperty("properties");
        Assert.True(identitySessionResponse.TryGetProperty("roles", out _));
        Assert.False(identitySessionResponse.TryGetProperty("permissions", out _));
        Assert.False(identitySessionResponse.TryGetProperty("permissionSnapshotVersion", out _));

        var identityUserAccountResponse = schemas.GetProperty("IdentityUserAccountResponse").GetProperty("properties");
        Assert.True(identityUserAccountResponse.TryGetProperty("roles", out _));
        Assert.False(identityUserAccountResponse.TryGetProperty("permissions", out _));
        Assert.False(identityUserAccountResponse.TryGetProperty("permissionSnapshotVersion", out _));

        var createIdentityUserRequest = schemas.GetProperty("CreateIdentityUserRequest").GetProperty("properties");
        Assert.True(createIdentityUserRequest.TryGetProperty("roles", out _));
        Assert.False(createIdentityUserRequest.TryGetProperty("permissions", out _));

        var bootstrapManifest = schemas.GetProperty("BootstrapManifestResponse").GetProperty("properties");
        Assert.False(bootstrapManifest.TryGetProperty("permissions", out _));
    }

    [Fact]
    public void BootstrapManifestModuleResponseExposesModuleNamespace()
    {
        using var document = RepositoryFiles.ReadJsonDocument("contracts", "http", "openapi.v1.json");
        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");
        var moduleResponse = schemas.GetProperty("BootstrapManifestModuleResponse").GetProperty("properties");

        Assert.True(
            moduleResponse.TryGetProperty("moduleNamespace", out _),
            "BootstrapManifestModuleResponse must expose the moduleNamespace field.");
        Assert.False(
            moduleResponse.TryGetProperty("permissionNamespace", out _),
            "BootstrapManifestModuleResponse must not expose the legacy permissionNamespace field.");
        Assert.False(
            moduleResponse.TryGetProperty("permissions", out _),
            "BootstrapManifestModuleResponse must not reintroduce a 'permissions' field.");
    }

    [Fact]
    public void DisableableModuleOperationsAdvertiseServiceUnavailableProblemDetails()
    {
        using var document = RepositoryFiles.ReadJsonDocument("contracts", "http", "openapi.v1.json");
        var paths = document.RootElement.GetProperty("paths");
        var disableableModules = RepositoryFiles.ReadModuleApiAssemblies()
            .SelectMany(static assembly => assembly.ExportedTypes)
            .Where(static type => typeof(IApiModule).IsAssignableFrom(type))
            .Where(static type => type is { IsClass: true, IsAbstract: false })
            .Select(type => (IModule)(Activator.CreateInstance(type)
                ?? throw new InvalidOperationException($"Could not create module '{type.FullName}'.")))
            .Where(static module => module.Descriptor.CanBeDisabled)
            .OrderBy(static module => module.Key, StringComparer.Ordinal)
            .ToArray();

        if (disableableModules.Length == 0)
        {
            return;
        }

        foreach (var module in disableableModules)
        {
            var routePrefix = $"/api/v1{module.Descriptor.RoutePrefix}";
            var moduleOperations = EnumerateOperations(paths)
                .Where(operation => PathBelongsToModule(operation.Path, routePrefix))
                .ToArray();

            Assert.NotEmpty(moduleOperations);

            foreach (var operation in moduleOperations)
            {
                Assert.True(
                    operation.Responses.TryGetProperty("503", out var serviceUnavailable),
                    $"Expected {operation.Method.ToUpperInvariant()} {operation.Path} to advertise a 503 ProblemDetails response for disabled or disabling module state.");

                var schemaRef = serviceUnavailable
                    .GetProperty("content")
                    .GetProperty("application/problem+json")
                    .GetProperty("schema")
                    .GetProperty("$ref")
                    .GetString();

                Assert.Equal("#/components/schemas/ProblemDetails", schemaRef);
            }
        }
    }

    private static IEnumerable<(string Path, string Method, string OperationId, string Summary, string VersionSet, JsonElement Responses)> EnumerateOperations(JsonElement paths)
    {
        foreach (var path in paths.EnumerateObject())
        {
            foreach (var method in path.Value.EnumerateObject())
            {
                if (!IsHttpMethod(method.Name))
                {
                    continue;
                }

                yield return (
                    path.Name,
                    method.Name,
                    method.Value.GetProperty("operationId").GetString() ?? string.Empty,
                    method.Value.GetProperty("summary").GetString() ?? string.Empty,
                    method.Value.GetProperty("x-baseline-version-set").GetString() ?? string.Empty,
                    method.Value.GetProperty("responses"));
            }
        }
    }

    private static bool PathBelongsToModule(string path, string routePrefix)
    {
        return string.Equals(path, routePrefix, StringComparison.Ordinal)
            || path.StartsWith(routePrefix + "/", StringComparison.Ordinal);
    }

    private static bool IsHttpMethod(string value)
    {
        return value is "get" or "put" or "post" or "delete" or "options" or "head" or "patch" or "trace";
    }
}
