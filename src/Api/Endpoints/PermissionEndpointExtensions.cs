using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Perezosoft.Core.Abstractions;
using Perezosoft.Core.Authorization;

namespace Perezosoft.Api.Endpoints;

/// <summary>
/// Gates an endpoint (or a feature group) behind a tenant <see cref="Permission"/> (ADR-009). A feature
/// slice adds <c>.RequirePermission(Permission.Xxx)</c> to declare an authorization gate; a caller whose
/// role lacks the permission gets <b>403 Forbidden</b> — the authorization counterpart to
/// <see cref="EntitlementEndpointExtensions.RequireEntitlement{TBuilder}"/>'s 402 (payment). The check is
/// server-side via <see cref="IPermissionService"/> (the role→permission matrix) — never trust the client.
/// </summary>
public static class PermissionEndpointExtensions
{
    public static TBuilder RequirePermission<TBuilder>(this TBuilder builder, Permission permission)
        where TBuilder : IEndpointConventionBuilder
        => builder.WithMetadata(new RequiredTenantPermission(permission)).AddEndpointFilter(async (context, next) =>
        {
            var permissions = context.HttpContext.RequestServices.GetRequiredService<IPermissionService>();
            if (await permissions.HasAsync(permission, context.HttpContext.RequestAborted))
                return await next(context);

            return Results.Json(new PermissionDeniedResponse(
                "forbidden",
                permission.ToString(),
                $"This action requires the '{permission}' permission."), statusCode: StatusCodes.Status403Forbidden);
        });
}

/// <summary>
/// Endpoint metadata naming the permission <c>.RequirePermission(...)</c> gates on (Pkg 3.8): what lets a test read every
/// permission a host's endpoints require — the MVC attribute is metadata already — and check each one is registered.
/// </summary>
public sealed record RequiredTenantPermission(Permission Permission);

/// <summary>The 403 body: the shared <c>error</c>/<c>message</c> envelope plus which permission was
/// required. A NAMED record (v3 audit TR-5/R76) so the wire contract is reviewable — byte-identical to
/// the previous anonymous shape.</summary>
public sealed record PermissionDeniedResponse(string Error, string Permission, string Message);
