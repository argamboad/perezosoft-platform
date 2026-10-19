using System.Collections.Concurrent;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Time.Testing;
using Perezosoft.Client;
using Perezosoft.Client.Auth;

namespace Perezosoft.Testing.Ui;

/// <summary>
/// In-memory <see cref="ICulturePersistence"/> — starts unset. Set <see cref="WritesBlocked"/> to model a
/// store that's readable but NOT writable (quota/policy): writes are silently swallowed, exactly the
/// condition that could loop the locale reload forever (UX-2).
/// </summary>
public sealed class FakeCulturePersistence : ICulturePersistence
{
    public string? Value { get; private set; }
    public bool WritesBlocked { get; set; }
    public Task PersistAsync(string cultureCode) { if (!WritesBlocked) Value = cultureCode; return Task.CompletedTask; }
    public Task<string?> GetAsync() => Task.FromResult(Value);
    public Task ClearAsync() { Value = null; Cleared = true; return Task.CompletedTask; }
    public bool Cleared { get; private set; }
}
