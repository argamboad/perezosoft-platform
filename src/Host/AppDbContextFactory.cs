using Perezosoft.Infrastructure.Persistence;

namespace Perezosoft.Api;

/// <summary>
/// EF tooling's design-time factory for this app's context (Pkg 3.5). The work — the .env, the connection string, the
/// null tenant — is the platform's (<see cref="PlatformDesignTimeDbContextFactory{TContext}"/>, in the Api package);
/// a host only names its context. The same line in every app.
/// </summary>
public class AppDbContextFactory : PlatformDesignTimeDbContextFactory<AppDbContext>;
