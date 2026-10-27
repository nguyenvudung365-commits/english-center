namespace EnglishCenter.Modules.Identity.Application.Abstractions;

public interface IIdentityModuleInfo
{
    string ModuleName { get; }
    string Schema { get; }
}
