using System.Collections.Concurrent;
using System.Reflection;
using BuildingBlocks.Domain.Events;

namespace BuildingBlocks.Infrastructure.IntegrationEvents;

internal static class LoadedIntegrationEventTypeResolver
{
    private static readonly ConcurrentDictionary<string, Type> Cache = new(StringComparer.Ordinal);

    public static Type Resolve(string storedTypeName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storedTypeName);

        return TryResolve(storedTypeName)
            ?? throw new InvalidOperationException($"Could not resolve integration-event type '{storedTypeName}'.");
    }

    public static Type? TryResolve(string storedTypeName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storedTypeName);

        return Cache.TryGetValue(storedTypeName, out var cached)
            ? cached
            : ResolveUncachedOrNull(storedTypeName);
    }

    private static Type? ResolveUncachedOrNull(string storedTypeName)
    {
        foreach (var candidateAssembly in EnumerateCandidateAssemblies())
        {
            foreach (var candidateTypeName in EnumerateCandidateTypeNames(storedTypeName))
            {
                var eventType = candidateAssembly.GetType(candidateTypeName, throwOnError: false, ignoreCase: false);
                if (eventType is null)
                {
                    continue;
                }

                if (!typeof(IIntegrationEvent).IsAssignableFrom(eventType))
                {
                    continue;
                }

                Cache.TryAdd(storedTypeName, eventType);
                return eventType;
            }
        }

        return null;
    }

    private static IEnumerable<string> EnumerateCandidateTypeNames(string storedTypeName)
    {
        yield return storedTypeName;

        var legacySeparatorIndex = storedTypeName.IndexOf(',', StringComparison.Ordinal);
        if (legacySeparatorIndex <= 0)
        {
            yield break;
        }

        var legacyTypeName = storedTypeName[..legacySeparatorIndex].Trim();
        if (!string.IsNullOrWhiteSpace(legacyTypeName))
        {
            yield return legacyTypeName;
        }
    }

    private static IEnumerable<Assembly> EnumerateCandidateAssemblies()
    {
        var seenAssemblyNames = new HashSet<string>(StringComparer.Ordinal);
        var pendingAssemblies = new Queue<Assembly>(
            AppDomain.CurrentDomain.GetAssemblies()
                .Where(static assembly => !assembly.IsDynamic)
                .OrderBy(static assembly => assembly.GetName().Name, StringComparer.Ordinal));

        while (pendingAssemblies.Count > 0)
        {
            var assembly = pendingAssemblies.Dequeue();
            var assemblyName = assembly.GetName().Name;
            if (string.IsNullOrWhiteSpace(assemblyName) || !seenAssemblyNames.Add(assemblyName))
            {
                continue;
            }

            yield return assembly;

            foreach (var referencedAssemblyName in assembly.GetReferencedAssemblies()
                         .Where(static referencedAssembly => referencedAssembly.Name?.EndsWith(".PublicContracts", StringComparison.Ordinal) == true))
            {
                try
                {
                    pendingAssemblies.Enqueue(Assembly.Load(referencedAssemblyName));
                }
                catch
                {
                    // Resolution continues against the rest of the currently composed module set.
                }
            }
        }
    }
}
