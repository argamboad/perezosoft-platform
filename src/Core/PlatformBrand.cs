using System.Resources;

namespace Perezosoft.Core;

/// <summary>
/// How the platform presents the app in what it sends (Pkg 3.9, ADR-034 §4): <c>PlatformOptions.Brand</c>. The
/// transactional emails (sign-in code, magic link, invitation, notification copies) read their name, tagline, palette and
/// logo from here, and their copy from <see cref="Strings"/> ahead of the platform's own table. Every member starts at
/// the platform's brand, so a host that sets nothing sends exactly the mails it sent before. Checked when the host
/// builds its options: a blank name, a colour that is not a hex colour, or a logo that is not a PNG stops the host.
/// </summary>
public sealed class PlatformBrand
{
    /// <summary>The product name: the mail's title, the logo's alt text, the header and the footer. HTML-encoded when rendered.</summary>
    public string Name { get; set; } = "Perezosoft";

    /// <summary>The footer line under the name. HTML-encoded when rendered.</summary>
    public string Tagline { get; set; } = "Lazy reputation. Efficient engineering.";

    private string? _serviceName;

    /// <summary>
    /// The name traces, metrics and logs are filed under (the OpenTelemetry <c>service.name</c>, Pkg 3.9b). Unless set, the
    /// brand's name without whitespace plus <c>.Api</c> — <c>Perezosoft.Api</c> for the platform's own host, as before. The
    /// name itself also titles the OpenAPI document and is the issuer authenticator apps show for MFA.
    /// </summary>
    public string ServiceName
    {
        get => _serviceName ?? string.Concat(Name.Where(c => !char.IsWhiteSpace(c))) + ".Api";
        set => _serviceName = value;
    }

    /// <summary>The palette, as <c>#rrggbb</c> (or <c>#rgb</c>) values.</summary>
    public BrandColors Colors { get; } = new();

    /// <summary>The logo, as PNG bytes, sent inline (CID) at 56×56. <c>null</c>: the platform's logo, embedded in the package.</summary>
    public byte[]? Logo { get; set; }

    /// <summary>
    /// The app's string tables for the mails, first wins, ahead of the platform's <c>EmailStrings</c>: add a
    /// <see cref="ResourceManager"/> over the app's own resx holding only the keys it changes. Resolved one culture at a
    /// time (es-MX, then es, then neutral), and at each step the app's table before the platform's — so an app that
    /// overrides a key in English only still sends the platform's Spanish to a Spanish reader.
    /// </summary>
    public IList<ResourceManager> Strings { get; } = [];
}

/// <summary>The email palette (Pkg 3.9). Defaults are the platform's theme; each value must be a hex colour.</summary>
public sealed class BrandColors
{
    /// <summary>Buttons, the brand name, the code box text.</summary>
    public string Primary { get; set; } = "#465d4d";

    /// <summary>Headings.</summary>
    public string PrimaryDark { get; set; } = "#2f3d33";

    /// <summary>Links and the footer name.</summary>
    public string Accent { get; set; } = "#6b8a72";

    /// <summary>Footer text and the ignore note.</summary>
    public string AccentLight { get; set; } = "#9bb6a1";

    /// <summary>The page background and the code box.</summary>
    public string Surface { get; set; } = "#F5F7F4";

    /// <summary>Card and code-box borders.</summary>
    public string Border { get; set; } = "#DCE5DD";

    /// <summary>Body text.</summary>
    public string Ink { get; set; } = "#33403a";

    /// <summary>Small print.</summary>
    public string Muted { get; set; } = "#5a6b62";
}
