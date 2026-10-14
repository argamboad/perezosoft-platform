using Perezosoft.Api.Configuration;

namespace Perezosoft.Api.Endpoints;

/// <summary>
/// Shared scaffolding for vertical-slice feature endpoints. <see cref="MapTenantFeatureGroup"/> is
/// the one way a feature registers its routes: it returns a <c>MapGroup</c> already guarded by the
/// shared <see cref="AuthPolicies.TenantApi"/> policy, so a slice can't forget to require auth and
/// every feature is authenticated/tenant-scoped the same way (CONF-3). Use it instead of a raw
/// <c>app.MapGroup(prefix).RequireAuthorization(...)</c>.
/// <para>
/// It is also how an app registers a route prefix (Pkg 3.10): every endpoint of the group carries an
/// <see cref="AppRouteGroup"/>, and <see cref="RouteTableGuard"/> reads those at boot to keep two slices off one prefix
/// and every slice off the platform's surfaces.
/// </para>
/// </summary>
public static class FeatureEndpointExtensions
{
    public static RouteGroupBuilder MapTenantFeatureGroup(this IEndpointRouteBuilder app, string prefix) =>
        app.MapGroup(prefix).RequireAuthorization(AuthPolicies.TenantApi).WithMetadata(new AppRouteGroup(prefix));
}

/// <summary>
/// Endpoint metadata: the app route prefix a feature group claimed (Pkg 3.10). One instance per
/// <c>MapTenantFeatureGroup</c> call, so two calls naming one prefix are two claims.
/// </summary>
public sealed class AppRouteGroup(string prefix)
{
    /// <summary>The prefix as routing compares it: a leading slash, no trailing one, lower case.</summary>
    public string Prefix { get; } = RouteTableGuard.NormalizePrefix(prefix);
}
