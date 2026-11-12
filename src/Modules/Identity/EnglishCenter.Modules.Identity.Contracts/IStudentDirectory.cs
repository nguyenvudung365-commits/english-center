namespace EnglishCenter.Modules.Identity.Contracts;

// Minimal synchronous authority query; no credentials or persistence types cross this boundary.
public interface IStudentDirectory
{
    Task<bool> IsActiveStudentAsync(Guid userId, CancellationToken ct);
}
