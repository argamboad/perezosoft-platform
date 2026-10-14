using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Perezosoft.Api.Authentication;

namespace Perezosoft.Api.Configuration;

/// <summary>
/// The named authorization policies. <see cref="TenantApi"/> is the JWT-authenticated tenant policy shared
/// by platform controllers (<c>TenantApiControllerBase</c>) and feature minimal-API groups
/// (<c>MapTenantFeatureGroup</c>), so the "authenticated via the app JWT" rule is defined once (CONF-3).
/// <see cref="PublicApi"/> is the API-key-authenticated policy for the public surface (PUBAPI, ADR-015).
/// </summary>
public static class AuthPolicies
{
    public const string TenantApi = "TenantApi";
    public const string PublicApi = "PublicApi";

    // Each platform policy: an authenticated user, by exactly this scheme. The boot check compares against it.
    private static readonly IReadOnlyDictionary<string, string> PlatformPolicySchemes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [TenantApi] = JwtBearerDefaults.AuthenticationScheme,
            [PublicApi] = ApiKeyAuthenticationHandler.SchemeName,
        };

    public static IServiceCollection AddTenantApiAuthorization(this IServiceCollection services) =>
        services.AddAuthorization(options =>
        {
            options.AddPolicy(TenantApi, policy => policy
                .RequireAuthenticatedUser()
                .AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme));

            // Public API: authenticated by an API key (the ApiKey scheme). Only reachable when PUBAPI is
            // enabled (the routes aren't mapped otherwise); the policy is harmless when unused.
            options.AddPolicy(PublicApi, policy => policy
                .RequireAuthenticatedUser()
                .AddAuthenticationSchemes(ApiKeyAuthenticationHandler.SchemeName));
        });

    /// <summary>
    /// Adds an app's own authorization policy (Pkg 3.10, ADR-034 §4) — call it from <c>AddAppServices</c> and gate a slice with
    /// <c>.RequireAuthorization(name)</c>, after <c>MapTenantFeatureGroup</c>'s tenant policy. A platform policy's name is refused:
    /// ASP.NET lets a later <c>AddPolicy</c> REPLACE an earlier one of the same name (case-insensitively), which would rewrite
    /// how every platform tenant route authenticates.
    /// </summary>
    public static IServiceCollection AddAppPolicy(this IServiceCollection services, string name, Action<AuthorizationPolicyBuilder> configure)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("An app policy needs a name.");
        if (PlatformPolicySchemes.ContainsKey(name))
            throw new InvalidOperationException(
                $"'{name}' is a platform authorization policy; an app policy of that name would replace it for every platform route. Choose another name.");
        return services.Configure<AuthorizationOptions>(options => options.AddPolicy(name, configure));
    }

    /// <summary>
    /// Boot check (Pkg 3.10): the platform's policies are still the platform's — an authenticated user by their one scheme —
    /// however the host's registrations ran. A raw <c>AddPolicy("TenantApi", ...)</c> anywhere after the platform's would
    /// otherwise loosen every tenant route silently; this stops the host instead.
    /// </summary>
    public static void EnsurePlatformPoliciesIntact(IServiceProvider services)
    {
        var options = services.GetRequiredService<Microsoft.Extensions.Options.IOptions<AuthorizationOptions>>().Value;
        var broken = new List<string>();
        foreach (var (name, scheme) in PlatformPolicySchemes)
        {
            var policy = options.GetPolicy(name);
            var intact = policy is not null
                && policy.AuthenticationSchemes.SequenceEqual([scheme])
                && policy.Requirements.Count == 1
                && policy.Requirements[0] is Microsoft.AspNetCore.Authorization.Infrastructure.DenyAnonymousAuthorizationRequirement;
            if (!intact) broken.Add(name);
        }
        if (broken.Count > 0)
            throw new InvalidOperationException(
                $"The platform's authorization policies were replaced: {string.Join(", ", broken)}. Register app policies with " +
                "AddAppPolicy under their own names; a platform policy is never redefined.");
    }
}
