using System.Reflection;
using BuildingBlocks.Application.SharedReads;

namespace Integration.Tests.SharedRuntime;

internal static class SharedReadPolicyAccessor
{
    public static SharedReadPolicy ReadDeclaredPolicy(Type adapterType)
    {
        ArgumentNullException.ThrowIfNull(adapterType);

        var field = adapterType.GetField("Policy", BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Public)
            ?? throw new InvalidOperationException($"{adapterType.FullName} does not declare a static 'Policy' field.");

        var value = field.GetValue(null)
            ?? throw new InvalidOperationException($"{adapterType.FullName}.Policy is null.");

        return (SharedReadPolicy)value;
    }
}
