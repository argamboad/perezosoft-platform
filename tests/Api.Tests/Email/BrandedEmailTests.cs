using System.Resources;
using Microsoft.Extensions.DependencyInjection;
using Perezosoft.Api.Configuration;
using Perezosoft.Core;
using Perezosoft.Infrastructure.Email;

namespace Perezosoft.Api.Tests.Email;

/// <summary>
/// Pkg 3.9 (ADR-034 §4): the transactional emails are branded from the host's <c>PlatformOptions.Brand</c> — name,
/// tagline, palette, logo — and their copy from a chain of string tables in which the app's resx wins over the platform's,
/// one culture at a time. A host that sets nothing gets the platform's brand and copy, byte for byte.
/// </summary>
public class BrandedEmailTests
{
    private static readonly ResourceManager ScratchAppStrings =
        new("Perezosoft.Api.Tests.Email.ScratchAppEmailStrings", typeof(BrandedEmailTests).Assembly);

    private static BrandedEmail Default => new(new PlatformOptions().Brand);

    [Fact]
    public void TheDefaultBrand_IsThePlatforms()
    {
        var brand = new PlatformOptions().Brand;
        Assert.Equal("Perezosoft", brand.Name);
        Assert.Equal("Lazy reputation. Efficient engineering.", brand.Tagline);
        Assert.Equal("#465d4d", brand.Colors.Primary);
        Assert.Null(brand.Logo); // the platform's own logo, embedded in the package
        Assert.Empty(brand.Strings);

        var otp = Default.Otp("123456", 10, BrandedEmail.ResolveCulture("en"));
        Assert.Contains("<title>Perezosoft</title>", otp.Html, StringComparison.Ordinal);
        Assert.Contains("#465d4d", otp.Html, StringComparison.Ordinal);
        var logo = Assert.Single(otp.InlineImages);
        Assert.Equal(("perezosoft-logo", "logo.png", "image/png"), (logo.ContentId, logo.FileName, logo.MediaType));
        Assert.True(logo.Content.Length > 1000, "probe: the embedded platform logo");
    }

    [Theory]
    [InlineData("Perezosoft", null, "Perezosoft.Api")]          // the platform's own host: unchanged
    [InlineData("Y el Vuelto", null, "YelVuelto.Api")]          // derived: the name without spaces
    [InlineData("Y el Vuelto", "vuelto-api", "vuelto-api")]     // set explicitly
    public void TheServiceName_DerivesFromTheBrand_UnlessSet(string name, string? serviceName, string expected) // Pkg 3.9b (#433)
    {
        var brand = new PlatformOptions().Brand;
        brand.Name = name;
        if (serviceName is not null) brand.ServiceName = serviceName;
        Assert.Equal(expected, brand.ServiceName);
    }

    [Fact]
    public void AnAppsBrand_RendersInTheMail()
    {
        byte[] appLogo = [0x89, 0x50, 0x4E, 0x47, 1, 2, 3];
        var options = new PlatformOptions();
        options.Brand.Name = "Vuelto";
        options.Brand.Tagline = "Change, counted.";
        options.Brand.Colors.Primary = "#123456";
        options.Brand.Logo = appLogo;

        var mail = new BrandedEmail(options.Brand).MagicLink("https://x.test/l", 15, BrandedEmail.ResolveCulture("en"));

        Assert.Contains("<title>Vuelto</title>", mail.Html, StringComparison.Ordinal);
        Assert.Contains("alt=\"Vuelto\"", mail.Html, StringComparison.Ordinal);
        Assert.Contains("Change, counted.", mail.Html, StringComparison.Ordinal);
        Assert.Contains("bgcolor=\"#123456\"", mail.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("#465d4d", mail.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("Lazy reputation", mail.Html, StringComparison.Ordinal);
        Assert.Equal(appLogo, Assert.Single(mail.InlineImages).Content);
    }

    [Fact]
    public void TheBrandName_IsEncoded_NeverMarkup()
    {
        var options = new PlatformOptions();
        options.Brand.Name = "A&B <Co>";
        var mail = new BrandedEmail(options.Brand).Otp("1", 1, BrandedEmail.ResolveCulture("en"));
        Assert.Contains("A&amp;B &lt;Co&gt;", mail.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("<Co>", mail.Html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("red")]
    [InlineData("#12345")]
    [InlineData("#123456;background:url(x)")]
    [InlineData("")]
    public void AColourThatIsNotAHexColour_IsRefusedAtStartup(string colour)
    {
        // The palette is interpolated into style attributes: anything but a hex colour could carry CSS (or worse) into every mail.
        var ex = Assert.Throws<InvalidOperationException>(() =>
            new ServiceCollection().AddPlatformOptions(o => o.Brand.Colors.Primary = colour));
        Assert.Contains("Primary", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ABlankName_IsRefusedAtStartup() =>
        Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddPlatformOptions(o => o.Brand.Name = " "));

    [Fact]
    public void TheAppsStrings_Win_OneCultureAtATime()
    {
        var options = new PlatformOptions();
        options.Brand.Strings.Add(ScratchAppStrings);
        var mails = new BrandedEmail(options.Brand);
        var en = BrandedEmail.ResolveCulture("en");
        var es = BrandedEmail.ResolveCulture("es");

        Assert.Equal("Your Vuelto code", mails.Otp("1", 1, en).Subject);                        // the app's English
        Assert.Equal("Tu código de verificación", mails.Otp("1", 1, es).Subject);               // no app Spanish → the platform's Spanish, not the app's English
        Assert.Equal("Únete a una billetera de Vuelto", mails.Invitation("u", "t", es).Subject); // the app's Spanish
        Assert.Equal("Únete a una billetera de Vuelto", mails.Invitation("u", "t", BrandedEmail.ResolveCulture("es-MX")).Subject); // es-MX walks to the app's es
        Assert.Equal("Join a Vuelto wallet", mails.Invitation("u", "t", en).Subject);
        Assert.Equal("Your sign-in link", mails.MagicLink("l", 1, en).Subject);                 // a key the app left alone
        Assert.Equal("Tu enlace de acceso", mails.MagicLink("l", 1, es).Subject);
    }

    [Fact]
    public void AddPlatformOptions_RegistersTheHostsBrandedEmail()
    {
        var services = new ServiceCollection();
        services.AddPlatformOptions(o => o.Brand.Name = "Vuelto");
        using var provider = services.BuildServiceProvider();
        var mails = provider.GetRequiredService<BrandedEmail>();
        Assert.Contains("<title>Vuelto</title>", mails.Otp("1", 1, BrandedEmail.ResolveCulture("en")).Html, StringComparison.Ordinal);
        Assert.Same(mails, provider.GetRequiredService<BrandedEmail>());
    }
}
