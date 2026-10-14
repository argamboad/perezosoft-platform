using Perezosoft.Core;
using Perezosoft.Core.Authorization;

namespace Perezosoft.Api.Tests.Infrastructure;

/// <summary>
/// The role→permission matrix a platform host builds from its options (Pkg 3.8): the platform's default registry, unless
/// a test registers more.
/// </summary>
internal static class TestPermissions
{
    /// <summary>The platform's ten permissions with their ADR-009 rows, as <c>new PlatformOptions()</c> carries them.</summary>
    public static RolePermissions Default { get; } = new(new PlatformOptions().Permissions);
}
