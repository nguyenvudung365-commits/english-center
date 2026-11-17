namespace EnglishCenter.Modules.Identity.Contracts;

public sealed record StudentDisplay(Guid StudentId, string FullName, string? StudentCode);

public interface IStudentDisplayDirectory
{
    Task<IReadOnlyList<StudentDisplay>> FindActiveAsync(IReadOnlyCollection<Guid> studentIds, CancellationToken ct);
}
