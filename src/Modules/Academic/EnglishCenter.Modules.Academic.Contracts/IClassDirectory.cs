namespace EnglishCenter.Modules.Academic.Contracts;

public sealed record ClassIdentity(Guid Id, Guid TeacherId);
public interface IClassDirectory
{
    Task<ClassIdentity?> FindClassAsync(Guid classId, CancellationToken ct);
}
