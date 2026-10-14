using System.Globalization;
using System.Net;
using System.Resources;
using System.Text.RegularExpressions;
using Perezosoft.Core;
using Perezosoft.Core.Abstractions;

namespace Perezosoft.Infrastructure.Email;

/// <summary>A rendered branded email: localized subject, HTML, and the inline images it references.</summary>
public sealed record EmailBody(string Subject, string Html, IReadOnlyList<EmailInlineImage> InlineImages);

/// <summary>
/// Builds the branded HTML for transactional emails. Email HTML is its own
/// world — table-based layout, inline styles, web-safe fonts only — so this does NOT reuse
/// the app's CSS. The logo is embedded via CID (multipart/related), the one approach Gmail
/// and Outlook render reliably (they block data-URI images).
/// <para>
/// Localized per <c>culture</c> from <c>EmailStrings.resx</c>: emails are sent
/// server-side in an explicit culture (the requester's UI language for OTP/magic link, the
/// inviter's saved locale for invites), so it uses <see cref="ResourceManager"/> keyed by
/// culture rather than the ambient thread culture.
/// </para>
/// <para>
/// One per host since Pkg 3.9 (ADR-034 §4), built from <see cref="PlatformBrand"/> (<c>PlatformOptions.Brand</c>): the
/// name, tagline, palette and logo are the app's, and the copy comes from the app's string tables ahead of the platform's,
/// one culture at a time. The default brand renders exactly the mails the platform always sent.
/// </para>
/// </summary>
public sealed class BrandedEmail
{
    private const string LogoCid = "perezosoft-logo";
    private const string Font = "'Segoe UI',Helvetica,Arial,sans-serif";

    private static readonly ResourceManager PlatformStrings =
        new("Perezosoft.Infrastructure.Email.EmailStrings", typeof(BrandedEmail).Assembly);
    private static readonly CultureInfo DefaultCulture = CultureInfo.GetCultureInfo("en");
    private static readonly Regex HexColour = new("^#(?:[0-9A-Fa-f]{3}|[0-9A-Fa-f]{6})$", RegexOptions.Compiled);
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47];
    private static readonly Lazy<byte[]> PlatformLogo = new(LoadPlatformLogo);

    // The brand, as rendered: text HTML-encoded once, colours checked.
    private readonly string _name;
    private readonly string _tagline;
    private readonly string Green, GreenDark, Sage, SageLight, Surface, Border, Ink, Muted;
    private readonly byte[]? _logo;
    private readonly IReadOnlyList<ResourceManager> _strings;

    /// <summary>Builds the host's mails from its brand; refuses a brand that cannot be rendered safely.</summary>
    public BrandedEmail(PlatformBrand brand)
    {
        if (string.IsNullOrWhiteSpace(brand.Name))
            throw new InvalidOperationException("PlatformOptions.Brand.Name is blank — the mails need a name for their title, header and footer.");
        var c = brand.Colors;
        Green = Colour(nameof(c.Primary), c.Primary);
        GreenDark = Colour(nameof(c.PrimaryDark), c.PrimaryDark);
        Sage = Colour(nameof(c.Accent), c.Accent);
        SageLight = Colour(nameof(c.AccentLight), c.AccentLight);
        Surface = Colour(nameof(c.Surface), c.Surface);
        Border = Colour(nameof(c.Border), c.Border);
        Ink = Colour(nameof(c.Ink), c.Ink);
        Muted = Colour(nameof(c.Muted), c.Muted);
        if (brand.Logo is { } logo && (logo.Length < PngSignature.Length || !logo.AsSpan(0, PngSignature.Length).SequenceEqual(PngSignature)))
            throw new InvalidOperationException("PlatformOptions.Brand.Logo is not a PNG — the mails send it inline as image/png.");

        _name = WebUtility.HtmlEncode(brand.Name);
        _tagline = WebUtility.HtmlEncode(brand.Tagline ?? string.Empty);
        _logo = brand.Logo;
        _strings = [.. brand.Strings, PlatformStrings];
    }

    private static string Colour(string name, string? value) =>
        value is not null && HexColour.IsMatch(value)
            ? value
            : throw new InvalidOperationException(
                $"PlatformOptions.Brand.Colors.{name} is \"{value}\" — a colour is #rrggbb or #rgb (it is written into every mail's style attributes).");

    /// <summary>Resolves a locale code (e.g. "es") to a culture, defaulting to English.</summary>
    public static CultureInfo ResolveCulture(string? locale)
    {
        if (string.IsNullOrWhiteSpace(locale)) return DefaultCulture;
        try { return CultureInfo.GetCultureInfo(locale.Trim()); }
        catch (CultureNotFoundException) { return DefaultCulture; }
    }

    private string T(string key, CultureInfo culture, params object[] args)
    {
        var value = Lookup(key, culture) ?? key;
        return args.Length == 0 ? value : string.Format(culture, value, args);
    }

    // The override chain: walk the culture's fallbacks (es-MX → es → neutral) and, at each step, ask the app's tables
    // before the platform's. Asking each table with its own fallback instead would let an app's English-only override
    // shadow the platform's Spanish.
    private string? Lookup(string key, CultureInfo culture)
    {
        for (var c = culture; ; c = c.Parent)
        {
            foreach (var table in _strings)
                if (table.GetResourceSet(c, createIfNotExists: true, tryParents: false)?.GetString(key) is { } value)
                    return value;
            if (c.Equals(CultureInfo.InvariantCulture)) return null;
        }
    }

    /// <summary>The logo as an inline image; reference it from HTML as <c>cid:perezosoft-logo</c>.</summary>
    public EmailInlineImage Logo() => new(LogoCid, "logo.png", _logo ?? PlatformLogo.Value, "image/png");

    /// <summary>"Email me a 6-digit code" — the OTP code email.</summary>
    public EmailBody Otp(string code, int lifespanMinutes, CultureInfo culture) => Compose(
        T("Otp_Subject", culture),
        T("Otp_Preheader", culture, code),
        $"""
         {Heading(T("Otp_Heading", culture))}
         {Paragraph(T("Otp_Body", culture, lifespanMinutes))}
         <table role="presentation" width="100%" cellpadding="0" cellspacing="0"><tr><td align="center" style="padding:8px 0 4px;">
           <div style="display:inline-block;background:{Surface};border:1px solid {Border};border-radius:10px;padding:16px 26px;font-family:{Font};font-size:30px;font-weight:700;letter-spacing:.35em;color:{Green};">{code}</div>
         </td></tr></table>
         {IgnoreNote(T("Common_IgnoreNote", culture))}
         """);

    /// <summary>"Email me a magic link" — the passwordless sign-in link.</summary>
    public EmailBody MagicLink(string link, int lifespanMinutes, CultureInfo culture) => Compose(
        T("MagicLink_Subject", culture),
        T("MagicLink_Preheader", culture),
        $"""
         {Heading(T("MagicLink_Heading", culture))}
         {Paragraph(T("MagicLink_Body", culture, lifespanMinutes))}
         {Button(T("MagicLink_Button", culture), link)}
         {Paragraph(T("MagicLink_OrPaste", culture), small: true)}
         <p style="margin:0 0 8px;font-family:{Font};font-size:12px;line-height:1.5;color:{Sage};word-break:break-all;">{link}</p>
         {IgnoreNote(T("Common_IgnoreNote", culture))}
         """);

    /// <summary>Household invitation — join link plus the raw token fallback.</summary>
    public EmailBody Invitation(string joinUrl, string token, CultureInfo culture) => Compose(
        T("Invitation_Subject", culture),
        T("Invitation_Preheader", culture),
        $"""
         {Heading(T("Invitation_Heading", culture))}
         {Paragraph(T("Invitation_Body", culture))}
         {Button(T("Invitation_Button", culture), joinUrl)}
         {Paragraph(T("Invitation_OrToken", culture), small: true)}
         <table role="presentation" width="100%" cellpadding="0" cellspacing="0"><tr><td align="center" style="padding:0 0 4px;">
           <div style="display:inline-block;background:{Surface};border:1px solid {Border};border-radius:8px;padding:10px 16px;font-family:'Courier New',monospace;font-size:13px;color:{Green};word-break:break-all;">{token}</div>
         </td></tr></table>
         {IgnoreNote(T("Common_IgnoreNoteUnexpected", culture))}
         """);

    /// <summary>
    /// A branded in-app notification copy (NOTIFY-2). <paramref name="title"/>/<paramref name="body"/>
    /// are dynamic user-facing content, so they are HTML-encoded before interpolation — email content
    /// can't inject markup. The subject is the (plain) title.
    /// </summary>
    public EmailBody Notification(string title, string body, CultureInfo culture) => Compose(
        title,
        WebUtility.HtmlEncode(title),
        $"""
         {Heading(WebUtility.HtmlEncode(title))}
         {Paragraph(WebUtility.HtmlEncode(body))}
         {IgnoreNote(T("Common_IgnoreNote", culture))}
         """);

    // ── shell + pieces ────────────────────────────────────────────────────────

    private EmailBody Compose(string subject, string preheader, string inner) =>
        new(subject, Wrap(preheader, inner), [Logo()]);

    private string Wrap(string preheader, string inner) => $"""
        <!DOCTYPE html>
        <html lang="en"><head>
        <meta charset="utf-8">
        <meta name="viewport" content="width=device-width,initial-scale=1">
        <meta name="x-apple-disable-message-reformatting">
        <title>{_name}</title>
        </head>
        <body style="margin:0;padding:0;background:{Surface};">
        <div style="display:none;max-height:0;overflow:hidden;opacity:0;color:{Surface};">{preheader}</div>
        <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background:{Surface};">
        <tr><td align="center" style="padding:32px 16px;">
          <table role="presentation" cellpadding="0" cellspacing="0" style="width:100%;max-width:480px;">
            <tr><td align="center" style="padding:4px 0 24px;">
              <img src="cid:{LogoCid}" width="56" height="56" alt="{_name}" style="display:block;border:0;outline:none;text-decoration:none;">
              <div style="font-family:{Font};font-size:20px;font-weight:700;letter-spacing:-.01em;color:{Green};margin-top:8px;">{_name}</div>
            </td></tr>
            <tr><td style="background:#ffffff;border:1px solid {Border};border-radius:14px;padding:36px 32px;">
              {inner}
            </td></tr>
            <tr><td align="center" style="padding:24px 8px 0;font-family:{Font};font-size:12px;line-height:1.6;color:{SageLight};">
              <div style="font-weight:600;color:{Sage};">{_name}</div>
              <div>{_tagline}</div>
            </td></tr>
          </table>
        </td></tr>
        </table>
        </body></html>
        """;

    private string Heading(string text) =>
        $"""<h1 style="margin:0 0 10px;font-family:{Font};font-size:22px;font-weight:700;color:{GreenDark};">{text}</h1>""";

    private string Paragraph(string text, bool small = false) =>
        $"""<p style="margin:0 0 {(small ? "8" : "24")}px;font-family:{Font};font-size:{(small ? "13" : "15")}px;line-height:1.6;color:{(small ? Muted : Ink)};">{text}</p>""";

    // Bulletproof-ish button (table + bgcolor) for Outlook compatibility.
    private string Button(string label, string href) => $"""
        <table role="presentation" cellpadding="0" cellspacing="0" style="margin:4px auto 20px;"><tr>
          <td align="center" bgcolor="{Green}" style="border-radius:8px;">
            <a href="{href}" style="display:inline-block;padding:13px 34px;font-family:{Font};font-size:15px;font-weight:600;color:#ffffff;text-decoration:none;border-radius:8px;">{label}</a>
          </td>
        </tr></table>
        """;

    private string IgnoreNote(string text) =>
        $"""<p style="margin:24px 0 0;font-family:{Font};font-size:13px;line-height:1.5;color:{SageLight};">{text}</p>""";

    private static byte[] LoadPlatformLogo()
    {
        var asm = typeof(BrandedEmail).Assembly;
        var name = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("logo.png", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("Embedded email logo (logo.png) not found.");
        using var stream = asm.GetManifestResourceStream(name)!;
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }
}
