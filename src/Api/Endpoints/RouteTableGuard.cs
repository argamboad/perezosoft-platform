using System.Text;
using Microsoft.AspNetCore.Routing.Patterns;

namespace Perezosoft.Api.Endpoints;

/// <summary>
/// Boot-time backstop for route uniqueness (v4 T13, ADV-P4-13, R155). Two endpoints with the same HTTP method,
/// pattern and order boot fine and then turn every request to that route into a 500 (AmbiguousMatchException),
/// before authorization runs, so anonymous callers see it too. The CI gate <c>RouteGroupPrefixes_AreUnique</c>
/// catches a shared group prefix from source; this checks the real route table, whatever mapped it, and refuses
/// to start naming both endpoints.
/// <para>
/// Two routes collide when their patterns match the same requests the same way: parameter names don't count
/// (<c>{id}</c> = <c>{noteId}</c>), constraints and optionality do (routing ranks <c>{id:int}</c> above
/// <c>{slug}</c>), an endpoint with no method metadata answers every method, and a different
/// <see cref="RouteEndpoint.Order"/> (a fallback) is ranked, not ambiguous.
/// </para>
/// </summary>
public static class RouteTableGuard
{
    private const string AnyMethod = "*";

    /// <summary>
    /// The platform's route surfaces (Pkg 3.10): no app prefix may equal one, sit inside one or cover one. Declared rather
    /// than read from the route table because a gate leaves some unmapped (billing, the public API, webhooks) — an app that
    /// claimed one while its gate was off would break the day the gate opened. <c>PlatformPrefixCoverageTests</c> holds this
    /// list to the platform's real route table with every gate on.
    /// </summary>
    public static IReadOnlyList<string> PlatformPrefixes { get; } =
    [
        "/api/admin", "/api/apikeys", "/api/auth", "/api/billing", "/api/features", "/api/files", "/api/household",
        "/api/notifications", "/api/public", "/api/version", "/api/webhooks", "/health",
    ];

    /// <summary>A route prefix as routing compares it: a leading slash, no trailing one, lower case.</summary>
    public static string NormalizePrefix(string prefix) => "/" + prefix.Trim().Trim('/').ToLowerInvariant();

    public static void EnsureUnique(IEnumerable<Endpoint> endpoints)
    {
        var claimed = new Dictionary<(string Pattern, int Order, string Method), RouteEndpoint>();
        var collisions = new List<string>();
        var routeEndpoints = endpoints.OfType<RouteEndpoint>().ToList();
        collisions.AddRange(PrefixProblems(routeEndpoints));

        foreach (var endpoint in routeEndpoints)
        {
            var pattern = Shape(endpoint.RoutePattern);
            var methods = endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods is { Count: > 0 } m
                ? m.Select(x => x.ToUpperInvariant()).ToList()
                : [AnyMethod];

            foreach (var method in methods)
            {
                // A method-specific route collides with the any-method one on the same pattern, and vice versa.
                var rivals = method == AnyMethod
                    ? claimed.Where(c => c.Key.Pattern == pattern && c.Key.Order == endpoint.Order).Select(c => (c.Key.Method, c.Value))
                    : new[] { method, AnyMethod }
                        .Where(x => claimed.ContainsKey((pattern, endpoint.Order, x)))
                        .Select(x => (x, claimed[(pattern, endpoint.Order, x)]));

                foreach (var (rivalMethod, rival) in rivals.ToList())
                    collisions.Add($"{(method == AnyMethod ? rivalMethod : method)} {pattern}: " +
                                   $"'{rival.DisplayName}' and '{endpoint.DisplayName}'");

                claimed.TryAdd((pattern, endpoint.Order, method), endpoint);
            }
        }

        if (collisions.Count > 0)
            throw new InvalidOperationException(
                "Duplicate routes: every request to these would fail with AmbiguousMatchException, or a prefix is claimed " +
                "twice or on a platform surface. " + string.Join("; ", collisions.Distinct()));
    }

    // The app's prefixes, as its feature groups registered them (AppRouteGroup metadata, one instance per group): two groups
    // on one prefix, or a prefix on a platform surface, own each other's routes even before two of them collide exactly.
    private static IEnumerable<string> PrefixProblems(List<RouteEndpoint> endpoints)
    {
        var groups = endpoints.Select(e => e.Metadata.GetMetadata<AppRouteGroup>()).OfType<AppRouteGroup>().Distinct().ToList();
        foreach (var shared in groups.GroupBy(g => g.Prefix).Where(g => g.Count() > 1))
            yield return $"{shared.Key}: two feature groups claim this prefix (two slices would own each other's routes)";

        foreach (var prefix in groups.Select(g => g.Prefix).Distinct())
            foreach (var surface in PlatformPrefixes.Where(p => Overlaps(prefix, p)))
                yield return $"{prefix}: an app prefix on the platform surface {surface} — choose a prefix of the app's own";
    }

    private static bool Overlaps(string a, string b) =>
        a == b || a.StartsWith(b + "/", StringComparison.Ordinal) || b.StartsWith(a + "/", StringComparison.Ordinal);

    // The pattern as routing sees it: literals case-insensitive, parameters by kind and constraints, not by name.
    private static string Shape(RoutePattern pattern)
    {
        var sb = new StringBuilder();
        foreach (var segment in pattern.PathSegments)
        {
            if (sb.Length > 0) sb.Append('/');
            foreach (var part in segment.Parts)
                switch (part)
                {
                    case RoutePatternLiteralPart literal:
                        sb.Append(literal.Content.ToLowerInvariant());
                        break;
                    case RoutePatternSeparatorPart separator:
                        sb.Append(separator.Content);
                        break;
                    case RoutePatternParameterPart parameter:
                        sb.Append('{').Append(parameter.IsCatchAll ? "*" : "");
                        foreach (var policy in parameter.ParameterPolicies)
                            sb.Append(':').Append(policy.Content);
                        sb.Append(parameter.IsOptional ? "?" : "").Append('}');
                        break;
                }
        }
        return sb.ToString();
    }
}
