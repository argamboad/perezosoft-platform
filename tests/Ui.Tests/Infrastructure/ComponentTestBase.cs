using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Perezosoft.Shared.Ui.Resources;
using Perezosoft.Testing.Ui;

namespace Perezosoft.Ui.Tests.Infrastructure;

/// <summary>
/// Base for this repo's component tests: the packaged harness (<see cref="PlatformComponentTestBase"/>, Pkg 5.1c) plus
/// the one thing it cannot know — the seeded UI's own strings, served by the deterministic localizer.
/// </summary>
public abstract class ComponentTestBase : PlatformComponentTestBase
{
    protected ComponentTestBase()
    {
        Services.AddSingleton<IStringLocalizer<AppStrings>>(new FakeStringLocalizer<AppStrings>());
    }
}
