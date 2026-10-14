using Perezosoft.Core;
using Perezosoft.Core.Billing;

namespace Perezosoft.Api.Tests.Infrastructure;

/// <summary>
/// The plan catalog a platform host builds from its options (Pkg 3.7): the platform's defaults, unless a test says
/// otherwise. Tests read the catalog's numbers from here rather than copying them, as they did from the static catalog.
/// </summary>
internal static class TestPlans
{
    /// <summary>The platform's default plans (Free and Pro), as <c>new PlatformOptions()</c> carries them.</summary>
    public static PlanCatalog Default { get; } = From(new PlatformOptions());

    public static PlanCatalog From(PlatformOptions options) => new(options.Plans);
}
