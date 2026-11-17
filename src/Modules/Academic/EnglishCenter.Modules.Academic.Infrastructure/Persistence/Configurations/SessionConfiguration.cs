using EnglishCenter.Modules.Academic.Domain.Classes;
using EnglishCenter.Modules.Academic.Domain.Sessions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EnglishCenter.Modules.Academic.Infrastructure.Persistence.Configurations;

internal sealed class SessionConfiguration : IEntityTypeConfiguration<Session>
{
    public void Configure(EntityTypeBuilder<Session> builder)
    {
        builder.ToTable("sessions", table =>
        {
            table.HasCheckConstraint("ck_sessions_time", "ends_at_utc > starts_at_utc");
            table.HasCheckConstraint("ck_sessions_skills", "skills >= 1 AND skills <= 15");
            table.HasCheckConstraint("ck_sessions_type", "session_type IN ('Regular', 'MockTest', 'FinalTest')");
            table.HasCheckConstraint("ck_sessions_unit", "length(trim(unit)) BETWEEN 1 AND 100");
            table.HasCheckConstraint("ck_sessions_lesson", "length(trim(lesson)) BETWEEN 1 AND 200");
            table.HasCheckConstraint("ck_sessions_exam_slot", "exam_slot IS NULL OR (session_type <> 'Regular' AND length(trim(exam_slot)) BETWEEN 1 AND 100)");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Unit).HasMaxLength(Session.UnitMaxLength).IsRequired();
        builder.Property(x => x.Lesson).HasMaxLength(Session.LessonMaxLength).IsRequired();
        builder.Property(x => x.ExamSlot).HasMaxLength(Session.ExamSlotMaxLength);
        builder.Property(x => x.Skills).HasConversion<int>();
        builder.Property(x => x.SessionType).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.HasOne<Class>().WithMany().HasForeignKey(x => x.ClassId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.ClassId, x.StartsAtUtc });
        builder.HasIndex(x => x.StartsAtUtc);
    }
}
