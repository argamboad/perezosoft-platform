using NUnit.Framework;

namespace Perezosoft.Testing.E2E;

/// <summary>
/// Where the app under test listens when nothing overrides it (Pkg 5.1d). The package cannot know an app's ports, so
/// the app's E2E project fills these once, from its generated <c>LocalPorts</c> (local-ports.props, Arch A10), in a
/// module initializer. A runsettings TestRunParameter wins, then the environment variable of the same name
/// (<c>PLAYWRIGHT_BASE_URL</c>, <c>E2E_API_BASE_URL</c>, <c>MAILPIT_BASE_URL</c>) — how CI points the suite at its
/// single origin.
/// </summary>
public static class PlatformE2EHosts
{
    /// <summary>The Web app's origin (its https launch profile).</summary>
    public static string? Web { get; set; }

    /// <summary>The API's own origin, for calls a test makes as an external caller (the billing webhook).</summary>
    public static string? Api { get; set; }

    /// <summary>The Mailpit UI, whose REST API <see cref="Mailpit"/> reads.</summary>
    public static string? MailUi { get; set; }

    /// <summary>The runsettings parameter, else the environment variable, else the app's default.</summary>
    public static string Resolve(string name, string? appDefault) =>
        TestContext.Parameters.Get(name)
        ?? Environment.GetEnvironmentVariable(name)
        ?? appDefault
        ?? throw new InvalidOperationException(
            $"No {name}: set it in the runsettings or the environment, or fill PlatformE2EHosts from the app's E2E project.");
}
