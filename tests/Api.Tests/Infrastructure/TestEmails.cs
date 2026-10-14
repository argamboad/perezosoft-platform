using Perezosoft.Core;
using Perezosoft.Infrastructure.Email;

namespace Perezosoft.Api.Tests.Infrastructure;

/// <summary>The branded mails a default host builds (Pkg 3.9): the platform's brand and copy.</summary>
internal static class TestEmails
{
    public static BrandedEmail Default { get; } = new(new PlatformOptions().Brand);
}
