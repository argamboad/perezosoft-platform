using System.Collections.Concurrent;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Time.Testing;
using Perezosoft.Client;
using Perezosoft.Client.Auth;

namespace Perezosoft.Testing.Ui;

/// <summary>In-memory <see cref="ISessionStore"/>. Web-parity by default (cookie transport, no body token).</summary>
public sealed class FakeSessionStore(bool usesBodyTransport = false) : ISessionStore
{
    private string? _token;
    public bool UsesBodyTransport { get; } = usesBodyTransport;
    public Task<string?> GetRefreshTokenAsync() => Task.FromResult(_token);
    public Task SaveRefreshTokenAsync(string refreshToken) { _token = refreshToken; return Task.CompletedTask; }
    public Task ClearAsync() { _token = null; return Task.CompletedTask; }
}
