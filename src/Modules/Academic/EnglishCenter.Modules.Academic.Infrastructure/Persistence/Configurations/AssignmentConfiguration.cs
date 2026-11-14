using EnglishCenter.Modules.Academic.Domain.Assignments;
using EnglishCenter.Modules.Academic.Domain.Classes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EnglishCenter.Modules.Academic.Infrastructure.Persistence.Configurations;

internal sealed class AssignmentConfiguration : IEntityTypeConfiguration<Assignment>
{
    public void Configure(EntityTypeBuilder<Assignment> builder)
    {
        builder.ToTable("assignments", t =>
        {
            t.HasCheckConstraint("ck_assignments_window", "closes_at_utc > opens_at_utc");
            t.HasCheckConstraint("ck_assignments_kind", "kind IN ('InClass', 'Homework')");
        });
        builder.HasKey(x => x.Id); builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Title).HasMaxLength(Assignment.TitleMaxLength).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(Assignment.DescriptionMaxLength).IsRequired();
        builder.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.Unit).HasMaxLength(Assignment.UnitMaxLength);
        builder.Property(x => x.Lesson).HasMaxLength(Assignment.LessonMaxLength);
        builder.Property(x => x.Version).IsConcurrencyToken();
        // SessionId là tham chiếu logic tới buổi học của TV1; FK được thêm khi bảng sessions tồn tại.
        builder.HasIndex(x => x.SessionId);
        builder.HasOne<Class>().WithMany().HasForeignKey(x => x.ClassId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class SubmissionConfiguration : IEntityTypeConfiguration<Submission>
{
    public void Configure(EntityTypeBuilder<Submission> builder)
    {
        builder.ToTable("submissions", t =>
        {
            t.HasCheckConstraint("ck_submissions_score", "score IS NULL OR (CAST(score AS REAL) >= 0 AND CAST(score AS REAL) <= 10)");
            t.HasCheckConstraint("ck_submissions_grade_version", "grade_version >= 0");
        });
        builder.HasKey(x => x.Id); builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Revision).IsConcurrencyToken();
        builder.Property(x => x.Score).HasPrecision(3, 1);
        builder.Property(x => x.Feedback).HasMaxLength(Submission.FeedbackMaxLength);
        builder.HasIndex(x => new { x.AssignmentId, x.StudentId }).IsUnique();
        builder.HasOne<Assignment>().WithMany().HasForeignKey(x => x.AssignmentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(x => x.Files).WithOne().HasForeignKey(x => x.SubmissionId).IsRequired().OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.Files).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class SubmissionGradeConfiguration : IEntityTypeConfiguration<SubmissionGrade>
{
    public void Configure(EntityTypeBuilder<SubmissionGrade> builder)
    {
        builder.ToTable("submission_grades", t =>
        {
            t.HasCheckConstraint("ck_submission_grades_score", "CAST(score AS REAL) >= 0 AND CAST(score AS REAL) <= 10");
            t.HasCheckConstraint("ck_submission_grades_version", "version >= 1");
        });
        builder.HasKey(x => x.Id); builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Score).HasPrecision(3, 1);
        builder.Property(x => x.PreviousScore).HasPrecision(3, 1);
        builder.Property(x => x.Feedback).HasMaxLength(Submission.FeedbackMaxLength);
        // Một version một dòng: retry/race không nhân đôi lịch sử.
        builder.HasIndex(x => new { x.SubmissionId, x.Version }).IsUnique();
        builder.HasIndex(x => x.EventSentAt);
        builder.HasOne<Submission>().WithMany().HasForeignKey(x => x.SubmissionId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class AssignmentRecipientConfiguration : IEntityTypeConfiguration<AssignmentRecipient>
{
    public void Configure(EntityTypeBuilder<AssignmentRecipient> builder)
    {
        builder.ToTable("assignment_recipients", t =>
            t.HasCheckConstraint("ck_assignment_recipients_outcome", "outcome IN ('Pending', 'Submitted', 'Missed', 'Exempted')"));
        builder.HasKey(x => new { x.AssignmentId, x.StudentId });
        builder.Property(x => x.Outcome).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.HasIndex(x => x.StudentId);
        builder.HasOne<Assignment>().WithMany().HasForeignKey(x => x.AssignmentId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class SubmissionFileConfiguration : IEntityTypeConfiguration<SubmissionFile>
{
    public void Configure(EntityTypeBuilder<SubmissionFile> builder)
    {
        builder.ToTable("submission_files", t => t.HasCheckConstraint("ck_submission_files_size", "size_bytes > 0 AND size_bytes <= 20971520"));
        builder.HasKey(x => x.Id); builder.Property(x => x.Id).ValueGeneratedNever();
        builder.HasIndex(x => new { x.SubmissionId, x.FileId }).IsUnique();
        builder.Property(x => x.FileName).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ContentType).HasMaxLength(128).IsRequired();
        // FileId is a logical reference to Media; deliberately no cross-schema FK.
    }
}
