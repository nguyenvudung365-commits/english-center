using System.Globalization;
using EnglishCenter.Modules.Identity.Application.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace EnglishCenter.Modules.Identity.Infrastructure.Persistence;

internal sealed class StudentCodeGenerator(IdentityDbContext db) : IStudentCodeGenerator
{
    public async Task<string> AllocateAsync(DateTimeOffset createdAt, CancellationToken ct)
    {
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = "SELECT nextval('identity.student_code_sequence')";
            command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
            var number = Convert.ToInt64(await command.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture);
            if (number is < 1 or > 999999) throw new IdentityFailure(409, "Student code capacity is exhausted.");
            return string.Create(CultureInfo.InvariantCulture, $"HV{number:D6}-{createdAt.UtcDateTime:yy}");
        }
        catch (PostgresException ex) when (ex.SqlState == "2200H")
        {
            throw new IdentityFailure(409, "Student code capacity is exhausted.");
        }
        finally { await db.Database.CloseConnectionAsync(); }
    }
}
