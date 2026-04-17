using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using BuildingBlocks.Application.Dispatching;

namespace BuildingBlocks.Infrastructure.Dispatching;

public sealed class JsonCommandIdempotencyRequestHasher : ICommandIdempotencyRequestHasher
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        PropertyNamingPolicy = null,
        WriteIndented = false
    };

    public string ComputeHash<TRequest>(TRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var payload = JsonSerializer.SerializeToUtf8Bytes(request, typeof(TRequest), SerializerOptions);
        return Convert.ToHexString(SHA256.HashData(payload));
    }
}
