using EnglishCenter.Modules.Academic.Domain.Attendance;
using EnglishCenter.Modules.Academic.Domain.Classes;
using EnglishCenter.Modules.Academic.Domain.Sessions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using AttendanceEntity = EnglishCenter.Modules.Academic.Domain.Attendance.Attendance;

namespace EnglishCenter.Modules.Academic.Infrastructure.Persistence.Configurations;

internal sealed class AttendanceConfiguration : IEntityTypeConfiguration<AttendanceEntity>,
    IEntityTypeConfiguration<AttendanceRevision>, IEntityTypeConfiguration<AttendanceReplayAttempt>
{
    private const string StatusValues = "'Present', 'AbsentUnexcused', 'AbsentExcused', 'Late', 'LateOver20Min', 'LeftEarly'";

    public void Configure(EntityTypeBuilder<AttendanceEntity> builder)
    {
        builder.ToTable("attendance", table =>
        {
            table.HasCheckConstraint("ck_attendance_status", $"status IN ({StatusValues})");
            table.HasCheckConstraint("ck_attendance_revision", "revision >= 1");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.Revision).IsConcurrencyToken();
        builder.HasIndex(x => new { x.SessionId, x.StudentId }).IsUnique();
        builder.HasIndex(x => new { x.StudentId, x.ClassId });
        builder.HasOne<Class>().WithMany().HasForeignKey(x => x.ClassId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Session>().WithMany().HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.Restrict);
    }

    public void Configure(EntityTypeBuilder<AttendanceRevision> builder)
    {
        builder.ToTable("attendance_revisions", table =>
        {
            table.HasCheckConstraint("ck_attendance_revisions_status", $"status IN ({StatusValues})");
            table.HasCheckConstraint("ck_attendance_revisions_previous", $"previous_status IS NULL OR previous_status IN ({StatusValues})");
            table.HasCheckConstraint("ck_attendance_revisions_revision", "revision >= 1");
        });
        builder.HasKey(x => new { x.AttendanceId, x.Revision });
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.PreviousStatus).HasConversion<string>().HasMaxLength(20);
        builder.HasOne<AttendanceEntity>().WithMany().HasForeignKey(x => x.AttendanceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Class>().WithMany().HasForeignKey(x => x.ClassId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Session>().WithMany().HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.Restrict);
    }

    public void Configure(EntityTypeBuilder<AttendanceReplayAttempt> builder)
    {
        builder.ToTable("attendance_replay_attempts", table =>
        {
            table.HasCheckConstraint("ck_attendance_replay_reason", "length(trim(reason)) BETWEEN 1 AND 500");
            table.HasCheckConstraint("ck_attendance_replay_status", "dispatch_status IN ('Pending', 'Dispatched', 'Failed')");
            table.HasCheckConstraint("ck_attendance_replay_completion", "(dispatch_status = 'Pending' AND completed_at_utc IS NULL) OR (dispatch_status <> 'Pending' AND completed_at_utc IS NOT NULL)");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Reason).HasMaxLength(500).IsRequired();
        builder.Property(x => x.DispatchStatus).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.HasOne<AttendanceRevision>().WithMany().HasForeignKey(x => new { x.AttendanceId, x.Revision }).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.AttendanceId, x.RequestedAtUtc });
    }
}
