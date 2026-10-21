using Microsoft.EntityFrameworkCore;
using Perezosoft.Core.Abstractions;
using Perezosoft.Infrastructure.Persistence;
using Perezosoft.Testing;

namespace Perezosoft.Api.Tests.Infrastructure;

/// <summary>
/// Test DbContext = the real <see cref="AppDbContext"/> plus the <see cref="TestWidget"/> fixture set.
/// TestWidget is discovered from the DbSet property, so the base model's tenant-filter loop covers it
/// automatically; the write-stamping interceptor (wired in the base <c>OnConfiguring</c>) applies too.
/// </summary>
public sealed class TestAppDbContext(DbContextOptions<TestAppDbContext> options, ICurrentTenant currentTenant)
    : AppDbContext(options, currentTenant)
{
    public DbSet<TestWidget> TestWidgets => Set<TestWidget>();
    public DbSet<TestSharedWidget> TestSharedWidgets => Set<TestSharedWidget>();
}
