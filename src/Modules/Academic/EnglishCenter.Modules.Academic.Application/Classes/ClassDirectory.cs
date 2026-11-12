using EnglishCenter.Modules.Academic.Application.Abstractions;
using EnglishCenter.Modules.Academic.Contracts;

namespace EnglishCenter.Modules.Academic.Application.Classes;

internal sealed class ClassDirectory(IAcademicStore store) : IClassDirectory
{
    public async Task<ClassIdentity?> FindClassAsync(Guid classId, CancellationToken ct)
    {
        var item = await store.FindClassAsync(classId, ct);
        return item is null ? null : new(item.Id, item.TeacherId);
    }
}
