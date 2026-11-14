using Microsoft.EntityFrameworkCore;
using EnglishCenter.Modules.Finance.Domain.Invoices;

namespace EnglishCenter.Modules.Finance.Infrastructure.Persistence;

public sealed class FinanceDbContext : DbContext
{
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public FinanceDbContext(DbContextOptions<FinanceDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(FinanceSchema.Name);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(FinanceDbContext).Assembly);
        modelBuilder.Entity<Invoice>(b =>
        {
            b.ToTable("invoices", t =>
            {
                t.HasCheckConstraint("ck_invoice_amount", "amount >= 0");
                t.HasCheckConstraint("ck_invoice_status", "status IN ('Unpaid', 'Paid')");
                t.HasCheckConstraint("ck_invoice_payment", "(status = 'Unpaid' AND paid_at IS NULL AND confirmed_by IS NULL) OR (status = 'Paid' AND paid_at IS NOT NULL AND confirmed_by IS NOT NULL)");
            });
            b.HasKey(x => x.Id); b.HasIndex(x => x.EnrollmentId).IsUnique();
            b.HasIndex(x => x.InvoiceNo).IsUnique(); b.HasIndex(x => x.StudentId);
            b.Property(x => x.InvoiceNo).HasMaxLength(32); b.Property(x => x.Status).HasMaxLength(20);
            b.Property(x => x.Amount).HasPrecision(12, 0);
            b.Property(x => x.IssuedAt).HasConversion(x => x, x => DateTime.SpecifyKind(x, DateTimeKind.Utc));
            b.Property(x => x.PaidAt).HasConversion(x => x, x => x.HasValue ? DateTime.SpecifyKind(x.Value, DateTimeKind.Utc) : (DateTime?)null);
        });
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
            foreach (var p in entity.GetProperties())
                p.SetColumnName(System.Text.RegularExpressions.Regex.Replace(p.Name, "(?<!^)([A-Z])", "_$1").ToLowerInvariant());
    }
}
