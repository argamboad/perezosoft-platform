using Microsoft.EntityFrameworkCore;
using Perezosoft.Core.Entities;

namespace Perezosoft.Infrastructure.Persistence;

/// <summary>
/// The app's half of the context (Arch A1, R159): its <c>DbSet</c>s, and any model rule of its own in
/// <c>OnAppModelCreating</c>, which the platform's <c>OnModelCreating</c> calls last. The other half
/// (<c>AppDbContext.cs</c>) is the platform's and stays identical across repos: the platform sets, the tenant filter,
/// the interceptors. Entity configurations need no registration — they are discovered from this assembly — so a
/// slice adds its entity's set here and nothing else central. The platform ships the Notes sample as the content.
/// </summary>
public partial class AppDbContext
{
    // 🗑️ DELETE-ME: sample feature set (remove with the Features/Notes slice).
    public DbSet<Note> Notes => Set<Note>();

    // An app with model rules of its own implements the hook, e.g.
    //     partial void OnAppModelCreating(ModelBuilder builder) { … }
    // The platform has none, so the partial method stays unimplemented and the compiler drops the call.
}
