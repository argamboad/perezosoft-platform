using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.Extensions.DependencyInjection;
using Perezosoft.Api.Configuration;
using Perezosoft.Api.Controllers;
using Perezosoft.Core;

namespace Perezosoft.Api.Tests.Billing;

/// <summary>
/// Pkg 3.10 (ADR-034 §4): an app's own paid feature can sit behind the billing gate exactly like the platform's billing
/// surface — <c>options.BillingGatedControllers.Add(typeof(...))</c> in <c>ConfigurePlatform</c>, and with
/// <c>Billing:Enabled</c> off its controller drops out of the application model (a genuine 404, GATES-1).
/// </summary>
public class AppBillingGateTests
{
    /// <summary>A scratch app's paid-only controller ([NonController]: these tests build the application model by hand, and no
    /// test host may ever discover it).</summary>
    [NonController]
    [Route("api/reports-pro")]
    public sealed class ScratchAppProReportsController : ControllerBase
    {
        [HttpGet] public IActionResult Get() => Ok();
    }

    private sealed class NotAController;

    private static ApplicationModel ModelWith(params Type[] controllers)
    {
        var model = new ApplicationModel();
        foreach (var type in controllers) model.Controllers.Add(new ControllerModel(type.GetTypeInfo(), []));
        return model;
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void AnAppsGatedController_FollowsTheBillingGate(bool billingEnabled, bool present)
    {
        var options = new PlatformOptions();
        options.BillingGatedControllers.Add(typeof(ScratchAppProReportsController));
        var model = ModelWith(typeof(ScratchAppProReportsController), typeof(HouseholdController), typeof(BillingController));

        new BillingGateConvention(new BillingSettings { Enabled = billingEnabled }, options.BillingGatedControllers).Apply(model);

        Assert.Equal(present, model.Controllers.Any(c => c.ControllerType == typeof(ScratchAppProReportsController)));
        Assert.Equal(present, model.Controllers.Any(c => c.ControllerType == typeof(BillingController))); // the platform's, as before
        Assert.Contains(model.Controllers, c => c.ControllerType == typeof(HouseholdController));          // ungated stays
    }

    [Fact]
    public void ByDefault_OnlyThePlatformsControllersAreGated()
    {
        Assert.Empty(new PlatformOptions().BillingGatedControllers);
        Assert.Equal(BillingGateConvention.GatedControllers, new BillingGateConvention(new BillingSettings(), new PlatformOptions().BillingGatedControllers).Gated);
    }

    [Fact]
    public void ATypeThatIsNotAController_IsRefusedAtStartup()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            new BillingGateConvention(new BillingSettings(), [typeof(NotAController)]));
        Assert.Contains(nameof(NotAController), ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheHostsOptions_ReachTheConvention()
    {
        // AddPlatformOptions hands back the options it registered, so AddPlatform builds the convention from the same set.
        var options = new ServiceCollection().AddPlatformOptions(o => o.BillingGatedControllers.Add(typeof(ScratchAppProReportsController)));
        Assert.Contains(typeof(ScratchAppProReportsController), options.BillingGatedControllers);
    }
}
