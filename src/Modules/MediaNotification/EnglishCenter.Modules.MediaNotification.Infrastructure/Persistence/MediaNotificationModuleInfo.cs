using EnglishCenter.Modules.MediaNotification.Application.Abstractions;

namespace EnglishCenter.Modules.MediaNotification.Infrastructure.Persistence;

internal sealed class MediaNotificationModuleInfo : IMediaNotificationModuleInfo
{
    public string ModuleName => "MediaNotification";
    public string Schema => MediaNotificationSchema.Name;
}
