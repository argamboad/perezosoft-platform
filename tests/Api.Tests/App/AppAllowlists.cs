using Perezosoft.Core.Entities;

namespace Perezosoft.Api.Tests.App;

/// <summary>
/// The app's entries in the platform's architecture gates (Arch A1, R159). The gates in
/// <c>ArchitectureTests</c> and <c>DataProtectionIdentityTests</c> are the platform's and stay identical across
/// repos; what an app has to say to them — which contributor dissolves its entities, which of its files sends HTTP
/// to a fixed host, which purpose strings it protects with — is said here, typed (<c>nameof</c>: a renamed entity
/// fails to compile rather than silently dropping out of a canary). The platform ships the Notes sample's entries;
/// an app replaces them. A gate that grows a new allowlist adds a set here, and the add-a-slice checklist names it.
/// </summary>
internal static partial class AppAllowlists
{
    /// <summary>User-keyed entities an <c>IUserDataContributor</c> of the app erases (<c>EveryUserKeyedEntity_IsWiredIntoAccountErasure</c>).</summary>
    public static readonly IReadOnlySet<string> ErasureHandled = new HashSet<string>
    {
    };

    /// <summary>Tenant-owned entities an <c>ITenantDataContributor</c> of the app wipes and exports (<c>EveryTenantOwnedEntity_IsWiredIntoTenantDissolution</c>).</summary>
    public static readonly IReadOnlySet<string> DissolutionHandled = new HashSet<string>
    {
        nameof(Note), // NotesDataContributor — 🗑️ DELETE-ME with the sample slice
    };

    /// <summary>Nullable-TenantId entities whose lifecycle is pinned elsewhere than the four-facet spec (<c>EveryNullableTenantIdEntity_ShipsItsLifecycleSpec</c>), with the reason.</summary>
    public static readonly IReadOnlyDictionary<string, string> LifecycleSpecExceptions = new Dictionary<string, string>
    {
    };

    /// <summary>Entities carrying a <c>TenantId</c> that are scoped by convention rather than <c>ITenantScoped</c> (<c>EveryEntityWithATenantId_IsScopedOrAllowlisted</c>).</summary>
    public static readonly IReadOnlySet<string> TenantIdByConvention = new HashSet<string>
    {
    };

    /// <summary>Server-side files that send HTTP without the outbound URL guard, each with why its destinations are not attacker-influenced (<c>OutboundHttpSenders_RouteThroughTheUrlGuard_OrAreAllowlisted</c>).</summary>
    public static readonly IReadOnlyDictionary<string, string> OutboundHttpSenders = new Dictionary<string, string>
    {
    };

    /// <summary>Controllers that derive from neither tenant nor admin base, being anonymous or system surfaces (<c>EveryController_DerivesFromATenantOrAdminBase_OrIsAllowlisted</c>).</summary>
    public static readonly IReadOnlySet<string> ControllersOutsideTheBases = new HashSet<string>
    {
    };

    /// <summary>DataProtection purpose strings the app protects with, frozen because renaming one orphans what it encrypted (<c>DataProtectionIdentityTests</c>).</summary>
    public static readonly IReadOnlySet<string> DataProtectionPurposes = new HashSet<string>
    {
    };

    /// <summary>Source files that aggregate several small types and so do not declare one named for the file (<c>SourceFile_DeclaresATypeMatchingItsName</c>).</summary>
    public static readonly IReadOnlySet<string> TypeNameExceptions = new HashSet<string>
    {
    };
}
