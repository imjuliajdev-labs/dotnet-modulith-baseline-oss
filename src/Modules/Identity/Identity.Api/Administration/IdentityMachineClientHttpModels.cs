using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using Identity.Application.Administration;

namespace Identity.Api.Administration;

public sealed class CreateMachineClientRequest
{
    [SetsRequiredMembers]
    public CreateMachineClientRequest()
    {
        ClientName = string.Empty;
        Roles = [];
    }

    [Required]
    public required string ClientName { get; init; }

    [Required]
    public required string[] Roles { get; init; }
}

public sealed record SetMachineClientStatusRequest(bool IsActive);

public sealed record MachineClientCreatedResponse(
    string ClientId,
    string ClientName,
    string PlaintextSecret,
    string[] Roles,
    DateTimeOffset CreatedUtc)
{
    public static MachineClientCreatedResponse From(MachineClientCreated created)
    {
        ArgumentNullException.ThrowIfNull(created);

        return new MachineClientCreatedResponse(
            created.ClientId,
            created.ClientName,
            created.PlaintextSecret,
            created.Roles.ToArray(),
            created.CreatedUtc.ToDateTimeOffset());
    }
}

public sealed record MachineClientSecretRotatedResponse(
    string ClientId,
    string PlaintextSecret)
{
    public static MachineClientSecretRotatedResponse From(MachineClientSecretRotated rotated)
    {
        ArgumentNullException.ThrowIfNull(rotated);

        return new MachineClientSecretRotatedResponse(
            rotated.ClientId,
            rotated.PlaintextSecret);
    }
}

public sealed record MachineClientSummaryResponse(
    string ClientId,
    string ClientName,
    bool IsActive,
    bool IsRevoked,
    string[] Roles,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc,
    DateTimeOffset? RevokedUtc)
{
    public static MachineClientSummaryResponse From(MachineClientSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);

        return new MachineClientSummaryResponse(
            summary.ClientId,
            summary.ClientName,
            summary.IsActive,
            summary.IsRevoked,
            summary.Roles.ToArray(),
            summary.CreatedUtc.ToDateTimeOffset(),
            summary.UpdatedUtc.ToDateTimeOffset(),
            summary.RevokedUtc?.ToDateTimeOffset());
    }
}

public sealed record MachineClientSummaryListResponse(IReadOnlyCollection<MachineClientSummaryResponse> Clients)
{
    public static MachineClientSummaryListResponse From(MachineClientList list)
    {
        ArgumentNullException.ThrowIfNull(list);

        return new MachineClientSummaryListResponse(
            list.Clients.Select(MachineClientSummaryResponse.From).ToArray());
    }
}
