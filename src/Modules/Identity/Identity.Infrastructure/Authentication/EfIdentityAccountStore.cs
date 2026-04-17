using BuildingBlocks.Application.Results;
using Identity.Application.Administration;
using Identity.Application.Authentication;
using Identity.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Identity.Infrastructure.Authentication;

internal sealed class EfIdentityAccountStore : IIdentityAccountStore
{
    private readonly BuildingBlocks.Domain.Time.IClock _clock;
    private readonly IdentityPersistenceDbContext _dbContext;
    private readonly SignInManager<IdentityAccount> _signInManager;
    private readonly UserManager<IdentityAccount> _userManager;

    public EfIdentityAccountStore(
        BuildingBlocks.Domain.Time.IClock clock,
        IdentityPersistenceDbContext dbContext,
        SignInManager<IdentityAccount> signInManager,
        UserManager<IdentityAccount> userManager)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _signInManager = signInManager ?? throw new ArgumentNullException(nameof(signInManager));
        _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
    }

    public async ValueTask<Result<IdentityUserAccount>> CreateAsync(
        string userName,
        string displayName,
        string password,
        IReadOnlyCollection<string> roles,
        string preferredTimeZoneId,
        bool enabled,
        CancellationToken cancellationToken)
    {
        var normalizedUserName = IdentityAccountSupport.NormalizeUserName(userName);
        if (normalizedUserName is null)
        {
            return Result<IdentityUserAccount>.Failure(IdentityUserAdministrationErrors.UserNameRequired());
        }

        var normalizedDisplayName = IdentityAccountSupport.NormalizeDisplayName(displayName);
        if (normalizedDisplayName is null)
        {
            return Result<IdentityUserAccount>.Failure(IdentityUserAdministrationErrors.DisplayNameRequired());
        }

        var providedPassword = IdentityAccountSupport.GetPasswordOrNull(password);
        if (providedPassword is null)
        {
            return Result<IdentityUserAccount>.Failure(IdentityUserAdministrationErrors.PasswordRequired());
        }

        var normalizedRoles = IdentityAccountSupport.NormalizeRoles(
            roles,
            IdentityUserAdministrationErrors.InvalidRole);
        if (normalizedRoles.IsFailure)
        {
            return Result<IdentityUserAccount>.Failure(normalizedRoles.Error);
        }

        var normalizedPreferredTimeZoneId = IdentityAccountSupport.NormalizePreferredTimeZoneId(preferredTimeZoneId);
        if (normalizedPreferredTimeZoneId.IsFailure)
        {
            return Result<IdentityUserAccount>.Failure(normalizedPreferredTimeZoneId.Error);
        }

        var now = _clock.GetCurrentInstant();
        var actorId = $"identity:user:{Guid.NewGuid():n}";
        var user = new IdentityAccount
        {
            Id = actorId,
            UserName = normalizedUserName,
            DisplayName = normalizedDisplayName,
            Enabled = enabled,
            LockoutEnabled = true,
            PreferredTimeZoneId = normalizedPreferredTimeZoneId.Value!,
            CreatedUtc = now,
            UpdatedUtc = now
        };

        var createResult = await _userManager.CreateAsync(user, providedPassword);
        if (!createResult.Succeeded)
        {
            return Result<IdentityUserAccount>.Failure(MapCreateError(createResult, normalizedUserName));
        }

        var rolesToAssign = normalizedRoles.Value!.Count > 0
            ? normalizedRoles.Value!
            : new[] { BuildingBlocks.Application.Authorization.IdentityRoles.User };

        foreach (var role in rolesToAssign)
        {
            var addRoleResult = await _userManager.AddToRoleAsync(user, role);
            if (!addRoleResult.Succeeded)
            {
                return Result<IdentityUserAccount>.Failure(IdentityUserAdministrationErrors.InvalidRole(role));
            }
        }

        return Result<IdentityUserAccount>.Success(await ToUserAccountAsync(user, cancellationToken));
    }

    public async ValueTask<Result<IdentityActorSession>> GetCurrentAsync(string actorId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorId);

        var user = await _dbContext.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(record => record.Id == actorId, cancellationToken);

        if (user is null || !user.Enabled)
        {
            return Result<IdentityActorSession>.Failure(IdentityAuthenticationErrors.CurrentActorNotFound());
        }

        return Result<IdentityActorSession>.Success(await ToSessionAsync(user));
    }

    public async ValueTask<string?> GetPreferredTimeZoneIdAsync(string actorId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorId);

        return await _dbContext.Users
            .AsNoTracking()
            .Where(record => record.Id == actorId)
            .Select(record => record.PreferredTimeZoneId)
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async ValueTask<IReadOnlyCollection<IdentityUserAccount>> ListAsync(CancellationToken cancellationToken)
    {
        var rows = await (
            from user in _dbContext.Users.AsNoTracking()
            join userRole in _dbContext.UserRoles.AsNoTracking() on user.Id equals userRole.UserId into userRoleGroup
            from userRole in userRoleGroup.DefaultIfEmpty()
            join role in _dbContext.Roles.AsNoTracking() on userRole.RoleId equals role.Id into roleGroup
            from role in roleGroup.DefaultIfEmpty()
            orderby user.NormalizedUserName, user.Id, role.Name
            select new
            {
                user.Id,
                user.UserName,
                user.DisplayName,
                user.Enabled,
                user.PreferredTimeZoneId,
                user.CreatedUtc,
                user.UpdatedUtc,
                RoleName = role != null ? role.Name : null
            })
            .ToArrayAsync(cancellationToken);

        if (rows.Length == 0)
        {
            return Array.Empty<IdentityUserAccount>();
        }

        var users = rows
            .GroupBy(row => new
            {
                row.Id,
                row.UserName,
                row.DisplayName,
                row.Enabled,
                row.PreferredTimeZoneId,
                row.CreatedUtc,
                row.UpdatedUtc
            })
            .Select(group => new IdentityUserAccount(
                group.Key.Id,
                group.Key.UserName ?? string.Empty,
                group.Key.DisplayName,
                group.Key.Enabled,
                group.Select(row => row.RoleName)
                    .Where(static role => !string.IsNullOrWhiteSpace(role))
                    .Select(static role => role!)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(static role => role, StringComparer.Ordinal)
                    .ToArray(),
                group.Key.PreferredTimeZoneId,
                group.Key.CreatedUtc,
                group.Key.UpdatedUtc))
            .ToArray();

        return users;
    }

    public async ValueTask<Result<IdentityUserAccount>> RevokeSessionsAsync(string actorId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorId);

        var user = await _userManager.FindByIdAsync(actorId);
        if (user is null)
        {
            return Result<IdentityUserAccount>.Failure(IdentityUserAdministrationErrors.AccountNotFound(actorId));
        }

        await _userManager.UpdateSecurityStampAsync(user);
        user.UpdatedUtc = _clock.GetCurrentInstant();
        await _userManager.UpdateAsync(user);

        return Result<IdentityUserAccount>.Success(await ToUserAccountAsync(user, cancellationToken));
    }

    public async ValueTask<Result<IdentityUserAccount>> ResetPasswordAsync(string actorId, string password, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorId);

        var providedPassword = IdentityAccountSupport.GetPasswordOrNull(password);
        if (providedPassword is null)
        {
            return Result<IdentityUserAccount>.Failure(IdentityUserAdministrationErrors.PasswordRequired());
        }

        var user = await _userManager.FindByIdAsync(actorId);
        if (user is null)
        {
            return Result<IdentityUserAccount>.Failure(IdentityUserAdministrationErrors.AccountNotFound(actorId));
        }

        var token = await _userManager.GeneratePasswordResetTokenAsync(user);
        var resetResult = await _userManager.ResetPasswordAsync(user, token, providedPassword);
        if (!resetResult.Succeeded)
        {
            return Result<IdentityUserAccount>.Failure(MapPasswordValidationError(resetResult));
        }

        // Clear any lockout state so the reset unblocks the account.
        await _userManager.SetLockoutEndDateAsync(user, null);
        await _userManager.ResetAccessFailedCountAsync(user);
        await _userManager.UpdateSecurityStampAsync(user);
        user.UpdatedUtc = _clock.GetCurrentInstant();
        await _userManager.UpdateAsync(user);

        return Result<IdentityUserAccount>.Success(await ToUserAccountAsync(user, cancellationToken));
    }

    public async ValueTask<Result<IdentityActorSession>> SetPreferredTimeZoneAsync(
        string actorId,
        string preferredTimeZoneId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorId);

        var normalizedPreferredTimeZoneId = IdentityAccountSupport.NormalizePreferredTimeZoneId(preferredTimeZoneId);
        if (normalizedPreferredTimeZoneId.IsFailure)
        {
            return Result<IdentityActorSession>.Failure(normalizedPreferredTimeZoneId.Error);
        }

        var user = await _userManager.FindByIdAsync(actorId);
        if (user is null)
        {
            return Result<IdentityActorSession>.Failure(IdentityUserAdministrationErrors.AccountNotFound(actorId));
        }

        user.PreferredTimeZoneId = normalizedPreferredTimeZoneId.Value!;
        user.UpdatedUtc = _clock.GetCurrentInstant();
        await _userManager.UpdateAsync(user);

        return Result<IdentityActorSession>.Success(await ToSessionAsync(user));
    }

    public async ValueTask<Result<IdentityUserAccount>> SetEnabledAsync(string actorId, bool enabled, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorId);

        var user = await _userManager.FindByIdAsync(actorId);
        if (user is null)
        {
            return Result<IdentityUserAccount>.Failure(IdentityUserAdministrationErrors.AccountNotFound(actorId));
        }

        user.Enabled = enabled;
        user.UpdatedUtc = _clock.GetCurrentInstant();

        if (enabled)
        {
            await _userManager.SetLockoutEndDateAsync(user, null);
            await _userManager.ResetAccessFailedCountAsync(user);
        }

        await _userManager.UpdateAsync(user);
        await _userManager.UpdateSecurityStampAsync(user);

        return Result<IdentityUserAccount>.Success(await ToUserAccountAsync(user, cancellationToken));
    }

    public async ValueTask<Result<IdentityUserAccount>> SetRolesAsync(
        string actorId,
        IReadOnlyCollection<string> roles,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorId);

        var normalizedRoles = IdentityAccountSupport.NormalizeRoles(
            roles,
            IdentityUserAdministrationErrors.InvalidRole);
        if (normalizedRoles.IsFailure)
        {
            return Result<IdentityUserAccount>.Failure(normalizedRoles.Error);
        }

        var user = await _userManager.FindByIdAsync(actorId);
        if (user is null)
        {
            return Result<IdentityUserAccount>.Failure(IdentityUserAdministrationErrors.AccountNotFound(actorId));
        }

        var current = await _userManager.GetRolesAsync(user);
        var target = normalizedRoles.Value!;

        var toRemove = current.Where(role => !target.Contains(role, StringComparer.Ordinal)).ToArray();
        var toAdd = target.Where(role => !current.Contains(role, StringComparer.Ordinal)).ToArray();

        if (toRemove.Length > 0)
        {
            var removeResult = await _userManager.RemoveFromRolesAsync(user, toRemove);
            if (!removeResult.Succeeded)
            {
                return Result<IdentityUserAccount>.Failure(IdentityUserAdministrationErrors.InvalidRole(string.Join(",", toRemove)));
            }
        }

        if (toAdd.Length > 0)
        {
            var addResult = await _userManager.AddToRolesAsync(user, toAdd);
            if (!addResult.Succeeded)
            {
                return Result<IdentityUserAccount>.Failure(IdentityUserAdministrationErrors.InvalidRole(string.Join(",", toAdd)));
            }
        }

        await _userManager.UpdateSecurityStampAsync(user);
        user.UpdatedUtc = _clock.GetCurrentInstant();
        await _userManager.UpdateAsync(user);

        return Result<IdentityUserAccount>.Success(await ToUserAccountAsync(user, cancellationToken));
    }

    public async ValueTask<Result<IdentityUserAccount>> UnlockAsync(string actorId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorId);

        var user = await _userManager.FindByIdAsync(actorId);
        if (user is null)
        {
            return Result<IdentityUserAccount>.Failure(IdentityUserAdministrationErrors.AccountNotFound(actorId));
        }

        await _userManager.SetLockoutEndDateAsync(user, null);
        await _userManager.ResetAccessFailedCountAsync(user);
        user.UpdatedUtc = _clock.GetCurrentInstant();
        await _userManager.UpdateAsync(user);

        return Result<IdentityUserAccount>.Success(await ToUserAccountAsync(user, cancellationToken));
    }

    public async ValueTask<Result<IdentityActorSession>> ChangePasswordAsync(
        string actorId,
        string currentPassword,
        string newPassword,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorId);

        var providedCurrentPassword = IdentityAccountSupport.GetPasswordOrNull(currentPassword);
        if (providedCurrentPassword is null)
        {
            return Result<IdentityActorSession>.Failure(IdentityAuthenticationErrors.CurrentPasswordInvalid());
        }

        var providedNewPassword = IdentityAccountSupport.GetPasswordOrNull(newPassword);
        if (providedNewPassword is null)
        {
            return Result<IdentityActorSession>.Failure(IdentityUserAdministrationErrors.PasswordRequired());
        }

        var user = await _userManager.FindByIdAsync(actorId);
        if (user is null || !user.Enabled)
        {
            return Result<IdentityActorSession>.Failure(IdentityAuthenticationErrors.CurrentActorNotFound());
        }

        var changeResult = await _userManager.ChangePasswordAsync(user, providedCurrentPassword, providedNewPassword);
        if (!changeResult.Succeeded)
        {
            if (changeResult.Errors.Any(static error => string.Equals(error.Code, nameof(IdentityErrorDescriber.PasswordMismatch), StringComparison.OrdinalIgnoreCase)))
            {
                return Result<IdentityActorSession>.Failure(IdentityAuthenticationErrors.CurrentPasswordInvalid());
            }

            return Result<IdentityActorSession>.Failure(MapPasswordValidationError(changeResult));
        }

        // ChangePasswordAsync already bumps the security stamp; keep the timestamp in sync.
        user.UpdatedUtc = _clock.GetCurrentInstant();
        await _userManager.UpdateAsync(user);

        return Result<IdentityActorSession>.Success(await ToSessionAsync(user));
    }

    public async ValueTask<Result<IdentityActorSession>> StepUpAsync(string actorId, string password, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorId);

        var providedPassword = IdentityAccountSupport.GetPasswordOrNull(password);
        if (providedPassword is null)
        {
            return Result<IdentityActorSession>.Failure(IdentityAuthenticationErrors.InvalidCredentials());
        }

        var user = await _userManager.FindByIdAsync(actorId);
        if (user is null || !user.Enabled)
        {
            return Result<IdentityActorSession>.Failure(IdentityAuthenticationErrors.CurrentActorNotFound());
        }

        if (string.IsNullOrWhiteSpace(user.PasswordHash))
        {
            return Result<IdentityActorSession>.Failure(IdentityAuthenticationErrors.SeededAdminNotConfigured());
        }

        var signInResult = await _signInManager.CheckPasswordSignInAsync(user, providedPassword, lockoutOnFailure: true);
        if (signInResult.IsLockedOut)
        {
            return Result<IdentityActorSession>.Failure(IdentityAuthenticationErrors.AccountLocked());
        }

        return !signInResult.Succeeded
            ? Result<IdentityActorSession>.Failure(IdentityAuthenticationErrors.InvalidCredentials())
            : Result<IdentityActorSession>.Success(await ToSessionAsync(user));
    }

    public async ValueTask<Result<IdentityActorSession>> SignInAsync(string userName, string password, CancellationToken cancellationToken)
    {
        var normalizedUserName = IdentityAccountSupport.NormalizeUserName(userName);
        var providedPassword = IdentityAccountSupport.GetPasswordOrNull(password);
        if (normalizedUserName is null || providedPassword is null)
        {
            return Result<IdentityActorSession>.Failure(IdentityAuthenticationErrors.InvalidCredentials());
        }

        var user = await _userManager.FindByNameAsync(normalizedUserName);
        if (user is null || !user.Enabled)
        {
            return Result<IdentityActorSession>.Failure(IdentityAuthenticationErrors.InvalidCredentials());
        }

        if (string.IsNullOrWhiteSpace(user.PasswordHash))
        {
            return Result<IdentityActorSession>.Failure(IdentityAuthenticationErrors.SeededAdminNotConfigured());
        }

        var signInResult = await _signInManager.CheckPasswordSignInAsync(user, providedPassword, lockoutOnFailure: true);
        if (signInResult.IsLockedOut)
        {
            return Result<IdentityActorSession>.Failure(IdentityAuthenticationErrors.AccountLocked());
        }

        return !signInResult.Succeeded
            ? Result<IdentityActorSession>.Failure(IdentityAuthenticationErrors.InvalidCredentials())
            : Result<IdentityActorSession>.Success(await ToSessionAsync(user));
    }

    private static Error MapCreateError(IdentityResult result, string normalizedUserName)
    {
        if (result.Errors.Any(static error => string.Equals(error.Code, nameof(IdentityErrorDescriber.DuplicateUserName), StringComparison.OrdinalIgnoreCase)))
        {
            return IdentityUserAdministrationErrors.UserNameAlreadyExists(normalizedUserName);
        }

        if (result.Errors.Any(static error => error.Code.StartsWith("Password", StringComparison.OrdinalIgnoreCase)))
        {
            return MapPasswordValidationError(result);
        }

        var detail = string.Join("; ", result.Errors.Select(static error => error.Description));
        return new Error(
            "identity.user_create_failed",
            string.IsNullOrWhiteSpace(detail) ? "Creating the identity user failed." : detail,
            ErrorKind.Validation);
    }

    private static Error MapPasswordValidationError(IdentityResult result)
    {
        if (result.Errors.Any(static error => string.Equals(error.Code, nameof(IdentityErrorDescriber.PasswordTooShort), StringComparison.OrdinalIgnoreCase)))
        {
            return IdentityUserAdministrationErrors.PasswordTooShort(IdentityAccountSupport.MinimumPasswordLength);
        }

        var detail = string.Join("; ", result.Errors.Select(static error => error.Description));
        return IdentityUserAdministrationErrors.PasswordPolicyNotMet(
            string.IsNullOrWhiteSpace(detail)
                ? $"Password must be at least {IdentityAccountSupport.MinimumPasswordLength} characters long and contain at least {IdentityAccountSupport.RequiredUniqueCharacterCount} unique characters."
                : detail);
    }

    private async Task<IdentityActorSession> ToSessionAsync(IdentityAccount user)
    {
        var roles = await _userManager.GetRolesAsync(user);
        return new IdentityActorSession(
            user.Id,
            user.UserName ?? string.Empty,
            user.DisplayName,
            roles.OrderBy(role => role, StringComparer.Ordinal).ToArray(),
            user.PreferredTimeZoneId);
    }

    private async Task<IdentityUserAccount> ToUserAccountAsync(IdentityAccount user, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        var roles = await _userManager.GetRolesAsync(user);
        return new IdentityUserAccount(
            user.Id,
            user.UserName ?? string.Empty,
            user.DisplayName,
            user.Enabled,
            roles.OrderBy(role => role, StringComparer.Ordinal).ToArray(),
            user.PreferredTimeZoneId,
            user.CreatedUtc,
            user.UpdatedUtc);
    }
}
