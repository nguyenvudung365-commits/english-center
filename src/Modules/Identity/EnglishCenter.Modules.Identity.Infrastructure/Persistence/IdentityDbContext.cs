using Microsoft.EntityFrameworkCore;
using EnglishCenter.Modules.Identity.Domain.Users;

namespace EnglishCenter.Modules.Identity.Infrastructure.Persistence;

public sealed class IdentityDbContext : DbContext
{
    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshSession> RefreshSessions => Set<RefreshSession>();
    public IdentityDbContext(DbContextOptions<IdentityDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(IdentitySchema.Name);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(IdentityDbContext).Assembly);
        var users = modelBuilder.Entity<User>();
        users.ToTable("users");
        users.HasKey(x => x.Id);
        users.Property(x => x.Email).HasMaxLength(254).IsRequired();
        users.Property(x => x.NormalizedEmail).HasMaxLength(254).IsRequired();
        users.HasIndex(x => x.NormalizedEmail).IsUnique();
        users.Property(x => x.FullName).HasMaxLength(200).IsRequired();
        users.Property(x => x.Phone).HasMaxLength(32);
        users.Property(x => x.StudentCode).HasMaxLength(11);
        users.HasIndex(x => x.StudentCode).IsUnique().HasFilter("\"StudentCode\" IS NOT NULL");
        users.Property(x => x.ProfileVersion).HasDefaultValue(0L).IsConcurrencyToken();
        if (Database.IsNpgsql())
        {
            modelBuilder.HasSequence<long>("student_code_sequence", IdentitySchema.Name).HasMin(1).HasMax(999999);
            users.ToTable("users", table => table.HasCheckConstraint("ck_users_student_code",
                "(\"Role\" = 'Student' AND \"StudentCode\" IS NOT NULL AND \"StudentCode\" ~ '^HV[0-9]{6}-[0-9]{2}$' AND substring(\"StudentCode\" from 3 for 6) <> '000000' AND right(\"StudentCode\", 2) = to_char(\"CreatedAt\" AT TIME ZONE 'UTC', 'YY')) OR (\"Role\" <> 'Student' AND \"StudentCode\" IS NULL)"));
        }
        users.Property(x => x.Role).HasMaxLength(20).IsRequired();
        users.Property(x => x.PasswordHash).HasMaxLength(512).IsRequired();
        users.Property(x => x.Revision).IsConcurrencyToken();
        var sessions = modelBuilder.Entity<RefreshSession>();
        sessions.ToTable("refresh_sessions");
        sessions.HasKey(x => x.Id);
        sessions.Property(x => x.TokenHash).HasMaxLength(64).IsRequired();
        sessions.Property(x => x.RevokedAt).IsConcurrencyToken();
        sessions.HasIndex(x => x.TokenHash).IsUnique();
        sessions.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
