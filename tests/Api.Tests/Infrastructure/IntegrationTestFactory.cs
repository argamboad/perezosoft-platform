using Perezosoft.Infrastructure.Persistence;
using Perezosoft.Testing;

namespace Perezosoft.Api.Tests.Infrastructure;

/// <summary>
/// The platform's integration host: the packaged harness (<see cref="PlatformIntegrationFactory{TProgram, TContext}"/>,
/// Pkg 5.1a) over this repo's <c>Program</c> and <c>AppDbContext</c>. The app's hooks are in <c>App/AppTestComposition</c>.
/// </summary>
public sealed class IntegrationTestFactory : PlatformIntegrationFactory<Program, AppDbContext>
{
    /// <summary>Every config gate the app declares, by reflection — a new gate is on this list the day it is added.</summary>
    public static IReadOnlyList<string> AllGates() =>
        [.. Configuration.SettingsCatalog.SectionBound().Where(Configuration.SettingsCatalog.HasEnabledSwitch)
            .Select(t => Configuration.SettingsCatalog.SectionNameOf(t)!).Order()];
}

[CollectionDefinition(IntegrationCollection.Name)]
public sealed class IntegrationCollection : ICollectionFixture<IntegrationTestFactory>
{
    public const string Name = "integration";
}
