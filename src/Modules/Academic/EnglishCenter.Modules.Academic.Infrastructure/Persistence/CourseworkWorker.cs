using EnglishCenter.Modules.Academic.Application.Assignments;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EnglishCenter.Modules.Academic.Infrastructure.Persistence;

/// <summary>
/// Chạy định kỳ <see cref="CourseworkReconciler"/>. Cấu hình:
/// - Academic:Coursework:WorkerEnabled (mặc định true): phát lại event đã commit nhưng chưa gửi.
/// - Academic:Coursework:ResolveMissed (mặc định false): chốt nghĩa vụ/missed sau hạn. Chỉ bật sau khi đã đối soát dữ liệu staging (Q-07).
/// - Academic:Coursework:IntervalSeconds (mặc định 60, 10–3600).
/// Mỗi vòng dùng scope/DbContext mới; lỗi được log và thử lại ở vòng sau, không làm dừng host.
/// </summary>
internal sealed class CourseworkWorker(IServiceScopeFactory scopes, IConfiguration configuration, ILogger<CourseworkWorker> log)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (configuration.GetValue("Academic:Coursework:WorkerEnabled", true) is false) return;
        var resolve = configuration.GetValue("Academic:Coursework:ResolveMissed", false);
        var seconds = Math.Clamp(configuration.GetValue("Academic:Coursework:IntervalSeconds", 60), 10, 3600);
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(seconds));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<CourseworkReconciler>().RunOnceAsync(resolve, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception ex) { log.LogError(ex, "Coursework reconciliation failed; it will retry on the next tick."); }
        }
    }
}
