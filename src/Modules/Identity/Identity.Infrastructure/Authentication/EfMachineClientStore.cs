using System.Security.Cryptography;
using BuildingBlocks.Application.Results;
using Identity.Application.Administration;
using Identity.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Identity.Infrastructure.Authentication;

internal sealed class EfMachineClientStore : IMachineClientAdministrationService
{
    private readonly BuildingBlocks.Domain.Time.IClock _clock;
    private readonly IdentityPersistenceDbContext _dbContext;
    private readonly PasswordHasher<IdentityMachineClientRecord> _passwordHasher = new();

    public EfMachineClientStore(
        BuildingBlocks.Domain.Time.IClock clock,
        IdentityPersistenceDbContext dbContext)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public async ValueTask<Result<MachineClientCreated>> CreateAsync(
        string clientName,
        IReadOnlyCollection<string> roles,
        CancellationToken cancellationToken)
    {
        var normalizedClientName = NormalizeClientName(clientName);
        if (normalizedClientName is null)
        {
            return Result<MachineClientCreated>.Failure(IdentityMachineClientErrors.ClientNameRequired());
        }

        var normalizedRoles = NormalizeRoles(roles);
        if (normalizedRoles.IsFailure)
        {
            return Result<MachineClientCreated>.Failure(normalizedRoles.Error);
        }

        var effectiveRoles = normalizedRoles.Value!.Count > 0
            ? normalizedRoles.Value!.ToArray()
            : new[] { BuildingBlocks.Application.Authorization.IdentityRoles.Machine };

        var plaintextSecret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var now = _clock.GetCurrentInstant();
        var client = new IdentityMachineClientRecord
        {
            ClientId = $"machine:client:{Guid.NewGuid():n}",
            ClientName = normalizedClientName,
            NormalizedClientName = IdentityAccountSupport.NormalizeMachineClientNameKey(normalizedClientName),
            IsActive = true,
            IsRevoked = false,
            Roles = effectiveRoles,
            CreatedUtc = now,
            UpdatedUtc = now,
            RevokedUtc = null
        };
        client.SecretHash = _passwordHasher.HashPassword(client, plaintextSecret);

        _dbContext.MachineClients.Add(client);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException postgresException
            && postgresException.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return Result<MachineClientCreated>.Failure(IdentityMachineClientErrors.ClientNameAlreadyExists(normalizedClientName));
        }

        return Result<MachineClientCreated>.Success(new MachineClientCreated(
            client.ClientId,
            client.ClientName,
            plaintextSecret,
            client.Roles,
            client.CreatedUtc));
    }

    public async ValueTask<Result<MachineClientSummary>> GetAsync(string clientId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);

        var client = await _dbContext.MachineClients
            .AsNoTracking()
            .SingleOrDefaultAsync(record => record.ClientId == clientId, cancellationToken);

        return client is null
            ? Result<MachineClientSummary>.Failure(IdentityMachineClientErrors.ClientNotFound(clientId))
            : Result<MachineClientSummary>.Success(ToSummary(client));
    }

    public async ValueTask<IReadOnlyCollection<MachineClientSummary>> ListAsync(CancellationToken cancellationToken)
    {
        var clients = await _dbContext.MachineClients
            .AsNoTracking()
            .OrderBy(record => record.NormalizedClientName)
            .ThenBy(record => record.ClientId)
            .ToArrayAsync(cancellationToken);

        return clients.Select(ToSummary).ToArray();
    }

    public async ValueTask<Result<MachineClientSummary>> RevokeAsync(string clientId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);

        var client = await _dbContext.MachineClients.SingleOrDefaultAsync(record => record.ClientId == clientId, cancellationToken);
        if (client is null)
        {
            return Result<MachineClientSummary>.Failure(IdentityMachineClientErrors.ClientNotFound(clientId));
        }

        if (client.IsRevoked)
        {
            return Result<MachineClientSummary>.Failure(IdentityMachineClientErrors.ClientAlreadyRevoked(clientId));
        }

        var now = _clock.GetCurrentInstant();
        client.IsRevoked = true;
        client.IsActive = false;
        client.RevokedUtc = now;
        client.UpdatedUtc = now;

        await _dbContext.SaveChangesAsync(cancellationToken);
        return Result<MachineClientSummary>.Success(ToSummary(client));
    }

    public async ValueTask<Result<MachineClientSecretRotated>> RotateSecretAsync(string clientId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);

        var client = await _dbContext.MachineClients.SingleOrDefaultAsync(record => record.ClientId == clientId, cancellationToken);
        if (client is null)
        {
            return Result<MachineClientSecretRotated>.Failure(IdentityMachineClientErrors.ClientNotFound(clientId));
        }

        if (client.IsRevoked)
        {
            return Result<MachineClientSecretRotated>.Failure(IdentityMachineClientErrors.ClientAlreadyRevoked(clientId));
        }

        var plaintextSecret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        client.SecretHash = _passwordHasher.HashPassword(client, plaintextSecret);
        client.UpdatedUtc = _clock.GetCurrentInstant();

        await _dbContext.SaveChangesAsync(cancellationToken);
        return Result<MachineClientSecretRotated>.Success(new MachineClientSecretRotated(clientId, plaintextSecret));
    }

    public async ValueTask<Result<MachineClientSummary>> SetActiveAsync(string clientId, bool isActive, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);

        var client = await _dbContext.MachineClients.SingleOrDefaultAsync(record => record.ClientId == clientId, cancellationToken);
        if (client is null)
        {
            return Result<MachineClientSummary>.Failure(IdentityMachineClientErrors.ClientNotFound(clientId));
        }

        if (client.IsRevoked)
        {
            return Result<MachineClientSummary>.Failure(IdentityMachineClientErrors.ClientAlreadyRevoked(clientId));
        }

        client.IsActive = isActive;
        client.UpdatedUtc = _clock.GetCurrentInstant();

        await _dbContext.SaveChangesAsync(cancellationToken);
        return Result<MachineClientSummary>.Success(ToSummary(client));
    }

    private static Result<IReadOnlyCollection<string>> NormalizeRoles(IEnumerable<string>? roles)
    {
        return IdentityAccountSupport.NormalizeRoles(
            roles,
            IdentityMachineClientErrors.InvalidRole);
    }

    private static string? NormalizeClientName(string? clientName)
    {
        return string.IsNullOrWhiteSpace(clientName) ? null : clientName.Trim();
    }

    private static MachineClientSummary ToSummary(IdentityMachineClientRecord stored)
    {
        return new MachineClientSummary(
            stored.ClientId,
            stored.ClientName,
            stored.IsActive,
            stored.IsRevoked,
            stored.Roles,
            stored.CreatedUtc,
            stored.UpdatedUtc,
            stored.RevokedUtc);
    }
}
