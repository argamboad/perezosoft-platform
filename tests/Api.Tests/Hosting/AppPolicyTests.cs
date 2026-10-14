using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Perezosoft.Api.Configuration;

namespace Perezosoft.Api.Tests.Hosting;

/// <summary>
/// Pkg 3.10 (ADR-034 §4): an app adds its own authorization policies with <see cref="AuthPolicies.AddAppPolicy"/> and gates
/// its slices with them (<c>.RequireAuthorization("ProjectsEditor")</c>). ASP.NET would let an app add a policy anyway, but a
/// later <c>AddPolicy("TenantApi", ...)</c> silently REPLACES the platform's, loosening every tenant route at once: the hook
/// refuses the platform's names, and the boot check catches a replacement made any other way.
/// </summary>
public class AppPolicyTests
{
    private static IServiceProvider Build(Action<IServiceCollection>? app = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTenantApiAuthorization();
        app?.Invoke(services);
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task AScratchAppsPolicy_IsRegistered_NextToThePlatforms()
    {
        var provider = Build(s => s.AddAppPolicy("ProjectsEditor", p => p.RequireClaim("projects", "edit")));
        var policies = provider.GetRequiredService<IAuthorizationPolicyProvider>();

        var policy = await policies.GetPolicyAsync("ProjectsEditor");
        Assert.NotNull(policy);
        Assert.Contains(policy!.Requirements, r => r is Microsoft.AspNetCore.Authorization.Infrastructure.ClaimsAuthorizationRequirement);
        Assert.NotNull(await policies.GetPolicyAsync(AuthPolicies.TenantApi));
        AuthPolicies.EnsurePlatformPoliciesIntact(provider); // no throw
    }

    [Theory]
    [InlineData(AuthPolicies.TenantApi)]
    [InlineData("tenantapi")] // policy names are case-insensitive in ASP.NET
    [InlineData(AuthPolicies.PublicApi)]
    public void AnAppPolicy_NamedLikeAPlatformOne_IsRefused(string name)
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            new ServiceCollection().AddAppPolicy(name, p => p.RequireAssertion(_ => true)));
        Assert.Contains(name, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void APlatformPolicy_ReplacedAnyOtherWay_StopsTheHost()
    {
        // The hole the hook closes, made without the hook: a raw AddPolicy after the platform's wins in ASP.NET.
        var provider = Build(s => s.AddAuthorization(o => o.AddPolicy(AuthPolicies.TenantApi, p => p.RequireAssertion(_ => true))));

        var ex = Assert.Throws<InvalidOperationException>(() => AuthPolicies.EnsurePlatformPoliciesIntact(provider));
        Assert.Contains(AuthPolicies.TenantApi, ex.Message, StringComparison.Ordinal);
    }
}
