using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EnglishCenter.BuildingBlocks.Modules;

/// <summary>Hợp đồng đăng ký module (tùy chọn dùng ở sprint sau).</summary>
public interface IModule
{
    string Name { get; }
    IServiceCollection Register(IServiceCollection services, IConfiguration configuration);
}
