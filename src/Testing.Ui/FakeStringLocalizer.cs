using System.Collections.Concurrent;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Time.Testing;
using Perezosoft.Client;
using Perezosoft.Client.Auth;

namespace Perezosoft.Testing.Ui;

/// <summary>
/// Deterministic <see cref="IStringLocalizer{TResource}"/> for the app's strings (generic since Pkg 5.1c): returns the key itself as the text (with a
/// "[arg, …]" suffix when formatted), so component assertions pin a stable key + its arguments rather than
/// coupling to a shipped translation. Never "not found" — a missing key just renders as the key.
/// </summary>
public sealed class FakeStringLocalizer<TResource> : IStringLocalizer<TResource>
{
    public LocalizedString this[string name] => new(name, name, resourceNotFound: false);

    public LocalizedString this[string name, params object[] arguments]
    {
        get
        {
            var value = arguments.Length == 0 ? name : $"{name}[{string.Join(", ", arguments)}]";
            return new LocalizedString(name, value, resourceNotFound: false);
        }
    }

    public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
}
