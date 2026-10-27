using EnglishCenter.Modules.Academic.Application.Abstractions;

namespace EnglishCenter.Modules.Academic.Infrastructure.Persistence;

internal sealed class AcademicModuleInfo : IAcademicModuleInfo
{
    public string ModuleName => "Academic";
    public string Schema => AcademicSchema.Name;
}
