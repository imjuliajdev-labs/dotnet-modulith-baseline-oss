namespace Architecture.Tests;

public sealed class ConfigurationGovernanceGuardrailTests
{
    [Xunit.Fact]
    public void KnowledgeBaseInfrastructureBindsAndValidatesTypedModuleOptions()
    {
        var relativePath = "src/Modules/KnowledgeBase/KnowledgeBase.Infrastructure/KnowledgeBaseInfrastructureServiceCollectionExtensions.cs";
        var fullPath = RepositoryFiles.PathFromRoot(relativePath.Split('/'));
        if (!File.Exists(fullPath))
        {
            return;
        }

        var source = RepositoryFiles.ReadAllText(relativePath);

        Xunit.Assert.Contains("AddOptions<KnowledgeBaseInfrastructureOptions>()", source, StringComparison.Ordinal);
        Xunit.Assert.Contains("KnowledgeBaseInfrastructureOptions.SectionName", source, StringComparison.Ordinal);
        Xunit.Assert.Contains("ValidateOnStart()", source, StringComparison.Ordinal);
    }

    [Xunit.Fact]
    public void KnowledgeBaseRuntimeSettingsStayAuditedAndRecentAuthProtected()
    {
        var relativePath = "src/Modules/KnowledgeBase/KnowledgeBase.Application/Settings/KnowledgeBaseSettingsContracts.cs";
        var fullPath = RepositoryFiles.PathFromRoot(relativePath.Split('/'));
        if (!File.Exists(fullPath))
        {
            return;
        }

        var source = RepositoryFiles.ReadAllText(relativePath);

        Xunit.Assert.Contains("IRequireRecentAuthentication", source, StringComparison.Ordinal);
        Xunit.Assert.Contains("knowledge-base.settings.update", source, StringComparison.Ordinal);
    }

    [Xunit.Fact]
    public void IdentityInfrastructureRegistersBootstrapCredentialSafetyValidation()
    {
        var source = RepositoryFiles.ReadAllText("src/Modules/Identity/Identity.Infrastructure/IdentityInfrastructureServiceCollectionExtensions.cs");

        Xunit.Assert.DoesNotContain("IdentityBootstrapOptions", source, StringComparison.Ordinal);
        Xunit.Assert.DoesNotContain("BindConfiguration(\"Modules:Identity\")", source, StringComparison.Ordinal);
        Xunit.Assert.Contains("AddOptions<SeededAdminOptions>()", source, StringComparison.Ordinal);
        Xunit.Assert.Contains("BindConfiguration(\"Modules:Identity:SeededAdmin\")", source, StringComparison.Ordinal);
        Xunit.Assert.Contains("AddOptions<SeededMachineOptions>()", source, StringComparison.Ordinal);
        Xunit.Assert.Contains("BindConfiguration(\"Modules:Identity:SeededMachine\")", source, StringComparison.Ordinal);
        Xunit.Assert.Contains("IdentityBootstrapCredentialValidationHostedService", source, StringComparison.Ordinal);
    }

    [Xunit.Fact]
    public void IdentityInfrastructureConfiguresExplicitPasswordAndLockoutPolicy()
    {
        var source = RepositoryFiles.ReadAllText("src/Modules/Identity/Identity.Infrastructure/IdentityInfrastructureServiceCollectionExtensions.cs");

        Xunit.Assert.Contains("options.Password.RequiredLength = IdentityAccountSupport.MinimumPasswordLength", source, StringComparison.Ordinal);
        Xunit.Assert.Contains("options.Password.RequiredUniqueChars = IdentityAccountSupport.RequiredUniqueCharacterCount", source, StringComparison.Ordinal);
        Xunit.Assert.Contains("options.Lockout.AllowedForNewUsers = true", source, StringComparison.Ordinal);
        Xunit.Assert.Contains("options.Lockout.MaxFailedAccessAttempts = IdentityAccountSupport.MaxFailedAccessAttempts", source, StringComparison.Ordinal);
        Xunit.Assert.Contains("options.Lockout.DefaultLockoutTimeSpan = IdentityAccountSupport.DefaultLockoutTimeSpan", source, StringComparison.Ordinal);
        Xunit.Assert.Contains("IIdentityCurrentActorStepUpService", source, StringComparison.Ordinal);
    }

    [Xunit.Fact]
    public void IdentityCookieRenewalPreservesTheRecentAuthenticationInstantClaim()
    {
        var source = RepositoryFiles.ReadAllText("src/Modules/Identity/Identity.Infrastructure/Authentication/IdentityCookieAuthenticationEvents.cs");

        Xunit.Assert.Contains("HttpContextCurrentActorAccessor.AuthenticationInstantClaimType", source, StringComparison.Ordinal);
        Xunit.Assert.Contains("identity.AddClaim(new Claim(HttpContextCurrentActorAccessor.AuthenticationInstantClaimType, authenticationInstant));", source, StringComparison.Ordinal);
    }

    [Xunit.Fact]
    public void MachineAuthenticationHandlerDelegatesSeededAndPersistedCredentialValidation()
    {
        var source = RepositoryFiles.ReadAllText("src/Modules/Identity/Identity.Infrastructure/Authentication/MachineAuthenticationHandler.cs");

        Xunit.Assert.Contains("SeededMachineCredentialAuthenticator", source, StringComparison.Ordinal);
        Xunit.Assert.Contains("PersistedMachineClientAuthenticator", source, StringComparison.Ordinal);
        Xunit.Assert.Contains("TryAuthenticateAsync", source, StringComparison.Ordinal);
        Xunit.Assert.DoesNotContain("GetService<", source, StringComparison.Ordinal);
        Xunit.Assert.DoesNotContain("IdentityPersistenceDbContext", source, StringComparison.Ordinal);
    }

    [Xunit.Fact]
    public void IdentityUserListingProjectsRolesWithoutPerUserManagerLookups()
    {
        var source = RepositoryFiles.ReadAllText("src/Modules/Identity/Identity.Infrastructure/Authentication/EfIdentityAccountStore.cs");
        var listStart = source.IndexOf("public async ValueTask<IReadOnlyCollection<IdentityUserAccount>> ListAsync", StringComparison.Ordinal);
        var listEnd = source.IndexOf("public async ValueTask<Result<IdentityUserAccount>> RevokeSessionsAsync", listStart, StringComparison.Ordinal);

        Xunit.Assert.True(listStart >= 0 && listEnd > listStart, "Expected to isolate EfIdentityAccountStore.ListAsync source.");
        var listMethod = source[listStart..listEnd];

        Xunit.Assert.Contains("_dbContext.UserRoles", listMethod, StringComparison.Ordinal);
        Xunit.Assert.Contains("_dbContext.Roles", listMethod, StringComparison.Ordinal);
        Xunit.Assert.DoesNotContain("_userManager.GetRolesAsync", listMethod, StringComparison.Ordinal);
    }
}
