using EnglishCenter.Modules.Academic.Domain.Assignments;
using EnglishCenter.Modules.Academic.Domain.Classes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EnglishCenter.Modules.Academic.Infrastructure.Persistence.Configurations;

internal sealed class AssignmentConfiguration : IEntityTypeConfiguration<Assignment>
{
    public void Configure(EntityTypeBuilder<Assignment> builder)
    {
        builder.ToTable("assignments", t => t.HasCheckConstraint("ck_assignments_window", "closes_at_utc > opens_at_utc"));
        builder.HasKey(x => x.Id); builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Title).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(5000).IsRequired();
        builder.HasOne<Class>().WithMany().HasForeignKey(x => x.ClassId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class SubmissionConfiguration : IEntityTypeConfiguration<Submission>
{
    public void Configure(EntityTypeBuilder<Submission> builder)
    {
        builder.ToTable("submissions"); builder.HasKey(x => x.Id); builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Revision).IsConcurrencyToken();
        builder.HasIndex(x => new { x.AssignmentId, x.StudentId }).IsUnique();
        builder.HasOne<Assignment>().WithMany().HasForeignKey(x => x.AssignmentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(x => x.Files).WithOne().HasForeignKey(x => x.SubmissionId).IsRequired().OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.Files).UsePropertyAccessMode(PropertyAccessMode.Field);
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
