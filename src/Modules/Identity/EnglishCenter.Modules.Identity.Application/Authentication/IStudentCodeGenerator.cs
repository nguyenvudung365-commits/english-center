namespace EnglishCenter.Modules.Identity.Application.Authentication;

public interface IStudentCodeGenerator
{
    Task<string> AllocateAsync(DateTimeOffset createdAt, CancellationToken ct);
}
