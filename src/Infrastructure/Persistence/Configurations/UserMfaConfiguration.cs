using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Perezosoft.Core.Entities;

namespace Perezosoft.Infrastructure.Persistence.Configurations;

public class UserMfaConfiguration : IEntityTypeConfiguration<UserMfa>
{
    public void Configure(EntityTypeBuilder<UserMfa> m)
    {
        m.HasKey(x => x.Id);
        m.Property(x => x.EncryptedSecret).IsRequired();
        // One MFA row per user.
        m.HasIndex(x => x.UserId).IsUnique();
        // The database default MfaLockout gave the column when it added it to existing rows (AddColumn defaultValue: 0),
        // said in the model too (Pkg 5.8, #451): an app that builds the platform's tables from the model gets the same
        // column platform-schema.json records, not one without the default.
        m.Property(x => x.FailedAttemptCount).HasDefaultValue(0);
    }
}
