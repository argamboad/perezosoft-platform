using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Perezosoft.Api.Services;
using Perezosoft.Core.Authorization;
using Perezosoft.Core.Repositories;

namespace Perezosoft.Api.Authentication;

/// <summary>
/// Single tenant-permission gate for MVC actions (v2 audit B9-5 / ADR-009). Replaces the copy-pasted
/// controller preamble (<c>GetMembershipAsync</c> + null-check + <c>RequirePermission</c>) with one
/// declarative attribute that runs before the action. Exactly mirrors the old
/// <c>TenantApiControllerBase.RequirePermission</c> behavior and envelope:
/// <list type="bullet">
/// <item>no caller id / no membership → <b>401</b> <c>{ "error": "invalid_token", ... }</c>;</item>
/// <item>membership role lacks the permission → <b>403</b> <c>{ "error": "forbidden", ... }</c>;</item>
/// <item>otherwise the action runs.</item>
/// </list>
/// Backed by the same <see cref="RolePermissions"/> matrix as the minimal-API <c>.RequirePermission(...)</c>
/// filter — one authorization source of truth. The action still resolves its own membership for its work.
/// <para>
/// Takes the permission's NAME (Pkg 3.8): an attribute argument must be a constant, and a permission is an open set now —
/// <c>nameof(Permission.ManageBilling)</c> for a platform one, the app's own constant for an app's. A name the host never
/// registered grants nothing (403), and <c>PermissionRegistrationGateTests</c> fails on it before it ships.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class RequireTenantPermissionAttribute(string permission, string forbiddenMessage)
    : Attribute, IAsyncActionFilter
{
    private readonly Permission _permission = new(permission);
    private readonly string _forbiddenMessage = forbiddenMessage;

    /// <summary>The permission this action requires.</summary>
    public Permission Permission => _permission;

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var http = context.HttpContext;
        var tenants = http.RequestServices.GetRequiredService<ITenantRepository>();

        // Resolve the membership for the caller's JWT tenant, never an arbitrary one (LB-ADM-3).
        var membership = http.User.GetUserId() is { } userId && http.User.GetTenantId() is { } tenantId
            ? await tenants.GetMembershipAsync(userId, tenantId, http.RequestAborted)
            : null;

        if (membership is null)
        {
            context.Result = Envelope(StatusCodes.Status401Unauthorized,
                new ErrorResponse("invalid_token", "Invalid user identity"));
            return;
        }

        if (!http.RequestServices.GetRequiredService<RolePermissions>().Grants(membership.Role, _permission))
        {
            context.Result = Envelope(StatusCodes.Status403Forbidden,
                new ErrorResponse("forbidden", _forbiddenMessage));
            return;
        }

        await next();
    }

    // Same serialization path controllers use (ObjectResult → camelCase JSON envelope).
    private static ObjectResult Envelope(int statusCode, ErrorResponse body) =>
        new(body) { StatusCode = statusCode };
}
