namespace Identity.Domain;

// Assembly marker for Identity.Domain. The Identity module's domain rules are expressed
// as invariants on ASP.NET Core Identity entities (IdentityAccount, IdentityRole)
// which live in Identity.Infrastructure alongside their EF persistence configuration.
// This marker keeps the standard five-project module shape while Identity relies on the
// framework-owned user/role types instead of a custom domain aggregate.
internal static class IdentityAssemblyMarker;
