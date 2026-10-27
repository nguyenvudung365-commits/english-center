using EnglishCenter.Modules.Identity.Application.Abstractions;

namespace EnglishCenter.Modules.Identity.Infrastructure.Persistence;

internal sealed class IdentityModuleInfo : IIdentityModuleInfo
{
    public string ModuleName => "Identity";
    public string Schema => IdentitySchema.Name;
}
