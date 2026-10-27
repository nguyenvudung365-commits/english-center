using EnglishCenter.Modules.Enrollment.Application.Abstractions;

namespace EnglishCenter.Modules.Enrollment.Infrastructure.Persistence;

internal sealed class EnrollmentModuleInfo : IEnrollmentModuleInfo
{
    public string ModuleName => "Enrollment";
    public string Schema => EnrollmentSchema.Name;
}
