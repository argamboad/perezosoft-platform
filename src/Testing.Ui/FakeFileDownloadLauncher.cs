using System.Collections.Concurrent;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Time.Testing;
using Perezosoft.Client;
using Perezosoft.Client.Auth;

namespace Perezosoft.Testing.Ui;

/// <summary>
/// Records what a component asked the host to download instead of touching the browser or the OS share
/// sheet. Part of the shared chassis (R70) so any page injecting <see cref="IFileDownloadLauncher"/> —
/// today Household's GDPR export — renders without each test re-registering a double.
/// </summary>
public sealed class FakeFileDownloadLauncher : IFileDownloadLauncher
{
    /// <summary>Every (url, fallbackFileName) pair handed over, in order.</summary>
    public List<(string Url, string FallbackFileName)> Launched { get; } = [];

    public Task LaunchAsync(string url, string fallbackFileName)
    {
        Launched.Add((url, fallbackFileName));
        return Task.CompletedTask;
    }
}
