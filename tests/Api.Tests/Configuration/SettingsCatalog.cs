using System.Reflection;

namespace Perezosoft.Api.Tests.Configuration;

/// <summary>
/// The app's settings classes, found by reflection, for the gates that must see every one of them: the config
/// catalog (<c>DocAndConfigSyncTests</c>, v4 T6) and the empty-config posture (<see cref="ConfigPostureTests"/>, v4 T7).
/// A class named <c>*Settings</c> is <b>section-bound</b> when it declares <c>public const string SectionName</c>;
/// the <c>SettingsProvider</c> classes instead implement a Core <c>I*Settings</c> interface and are built key by key.
/// </summary>
public static class SettingsCatalog
{
    public static IEnumerable<Type> All() =>
        new[] { typeof(global::Perezosoft.Api.Configuration.BillingSettings).Assembly,
                typeof(global::Perezosoft.Infrastructure.Email.SmtpSettings).Assembly,
                typeof(global::Perezosoft.Core.Abstractions.IEmailSender).Assembly }
            .Distinct()
            .SelectMany(a => a.GetTypes())
            .Where(t => t is { IsClass: true, IsAbstract: false, IsPublic: true } && t.Name.EndsWith("Settings", StringComparison.Ordinal));

    public static IEnumerable<Type> SectionBound() => All().Where(t => SectionNameOf(t) is not null);

    public static string? SectionNameOf(Type t) =>
        t.GetField("SectionName", BindingFlags.Public | BindingFlags.Static) is { IsLiteral: true } f
            ? (string?)f.GetRawConstantValue()
            : null;

    public static bool IsBuiltKeyByKey(Type t) =>
        t.GetInterfaces().Any(i => i.Name.StartsWith('I') && i.Name.EndsWith("Settings", StringComparison.Ordinal));

    /// <summary>A section-bound class with a <c>bool Enabled</c>: a feature switch.</summary>
    public static bool HasEnabledSwitch(Type t) => t.GetProperty("Enabled")?.PropertyType == typeof(bool);
}
