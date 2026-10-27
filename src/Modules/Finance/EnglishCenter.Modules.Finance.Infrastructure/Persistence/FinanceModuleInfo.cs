using EnglishCenter.Modules.Finance.Application.Abstractions;

namespace EnglishCenter.Modules.Finance.Infrastructure.Persistence;

internal sealed class FinanceModuleInfo : IFinanceModuleInfo
{
    public string ModuleName => "Finance";
    public string Schema => FinanceSchema.Name;
}
