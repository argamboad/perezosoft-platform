using System.Collections.Concurrent;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Time.Testing;
using Perezosoft.Client;
using Perezosoft.Client.Auth;

namespace Perezosoft.Testing.Ui;

/// <summary>In-memory <see cref="IThemePersistence"/> — starts unset (user never chose).</summary>
public sealed class FakeThemePersistence : IThemePersistence
{
    public string? Value { get; private set; }
    public Task PersistAsync(string theme) { Value = theme; return Task.CompletedTask; }
    public Task<string?> GetAsync() => Task.FromResult(Value);
    public Task ClearAsync() { Value = null; Cleared = true; return Task.CompletedTask; }
    public bool Cleared { get; private set; }
}
