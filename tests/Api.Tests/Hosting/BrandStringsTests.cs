using Microsoft.Extensions.DependencyInjection;
using Perezosoft.Api.Tests.Infrastructure;
using Perezosoft.Core;
using Swashbuckle.AspNetCore.Swagger;

namespace Perezosoft.Api.Tests.Hosting;

/// <summary>
/// Pkg 3.9b (#433): the brand strings the Api package used to compile in — the OpenAPI title, the MFA issuer, the telemetry
/// service name — come from the host's <c>PlatformOptions.Brand</c>. The platform's own host (no brand set) reads exactly as
/// before; the per-string cases with an app's brand live with each feature's tests (MfaServiceTests,
/// TelemetryLogsExportTests, BrandedEmailTests).
/// </summary>
[Collection(IntegrationCollection.Name)]
public class BrandStringsTests(IntegrationTestFactory factory)
{
    [Fact]
    public void TheOpenApiTitle_IsTheHostsBrand()
    {
        var brand = factory.Services.GetRequiredService<PlatformOptions>().Brand;
        var document = factory.Services.GetRequiredService<ISwaggerProvider>().GetSwagger("v1");

        Assert.Equal($"{brand.Name} API", document.Info.Title);
        Assert.Equal("Perezosoft API", document.Info.Title); // the platform's own host: unchanged
    }

    [Fact]
    public void ThePackage_CompilesInNoBrandString()
    {
        // The three call sites read the brand; a literal "Perezosoft" back in any of them is the regression this issue fixed.
        var root = RepoRoot();
        foreach (var file in new[] { "src/Api/Services/MfaService.cs", "src/Api/Observability/TelemetryExtensions.cs", "src/Api/PlatformHosting.cs" })
        {
            var code = string.Join('\n', File.ReadAllLines(Path.Combine(root, file)).Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal) && !l.TrimStart().StartsWith("///", StringComparison.Ordinal)));
            Assert.DoesNotMatch(@"""Perezosoft[ .A-Za-z]*""|""Perezosoft API|Title = ""Perezosoft", code);
        }
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !dir.EnumerateFiles("CLAUDE.md").Any()) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
    }
}
