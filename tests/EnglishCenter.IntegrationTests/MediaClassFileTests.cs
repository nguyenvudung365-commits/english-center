using EnglishCenter.BuildingBlocks.Security;
using EnglishCenter.Modules.MediaNotification.Application.Files;
using EnglishCenter.Modules.MediaNotification.Contracts;
using EnglishCenter.Modules.MediaNotification.Domain.Files;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnglishCenter.IntegrationTests;

// Tệp lớp do giáo viên/nhân viên đăng ("material" tài liệu, "exam" đề thi PDF): giới hạn theo vai trò và theo purpose.
public sealed partial class MediaFileServiceTests
{
    [Theory]
    [InlineData(Roles.Teacher, "material", "notes.docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document")]
    [InlineData(Roles.Teacher, "material", "notes.mp3", "audio/mpeg")]
    [InlineData(Roles.Staff, "material", "notes.pdf", "application/pdf")]
    [InlineData(Roles.Admin, "exam", "exam.pdf", "application/pdf")]
    [InlineData(Roles.Teacher, "exam", "exam.pdf", "application/pdf")]
    public async Task ClassFile_PresignAndConfirm_AllowedForTeachingRoles(string role, string purpose, string name, string mime)
    {
        var fixture = new Fixture(new AvatarAccounts());
        fixture.User.Role = role;
        var bytes = name.EndsWith(".pdf") ? Pdf : null;
        var ticket = await fixture.Service.PresignUploadAsync(new(name, mime, bytes?.Length ?? 100, purpose));
        Assert.Equal(purpose, fixture.Store.Snapshot(ticket.FileId).Purpose);
        if (bytes is null) return;
        fixture.Storage.Put(ticket.FileKey, bytes, mime);
        Assert.NotNull(await fixture.Service.ConfirmUploadAsync(ticket.FileId));
    }

    [Theory]
    [InlineData("material")]
    [InlineData("exam")]
    public async Task ClassFile_StudentCannotPresign_BeforePersistenceOrStorage(string purpose)
    {
        var fixture = new Fixture(new AvatarAccounts());
        fixture.User.Role = Roles.Student;
        await Fails(403, () => fixture.Service.PresignUploadAsync(new("exam.pdf", "application/pdf", 100, purpose)));
        Assert.Equal(0, fixture.Store.Calls);
        Assert.Empty(fixture.Storage.Calls);
    }

    [Theory]
    [InlineData("exam", "exam.docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document")]
    [InlineData("exam", "exam.png", "image/png")]
    [InlineData("material", "notes.exe", "application/octet-stream")]
    [InlineData("material", "notes.pdf", "image/png")]
    public async Task ClassFile_InvalidTypeRejected_ExamPromptMustBePdf(string purpose, string name, string mime)
    {
        var fixture = new Fixture(new AvatarAccounts());
        fixture.User.Role = Roles.Teacher;
        await Fails(400, () => fixture.Service.PresignUploadAsync(new(name, mime, 100, purpose)));
        Assert.Equal(0, fixture.Store.Calls);
    }

    [Fact]
    public async Task ClassFile_PresignRequiresActiveAccountInRole()
    {
        var accounts = new AvatarAccounts { Active = false };
        var fixture = new Fixture(accounts);
        fixture.User.Role = Roles.Teacher;
        await Fails(403, () => fixture.Service.PresignUploadAsync(new("exam.pdf", "application/pdf", 100, "exam")));
        Assert.Equal(0, fixture.Store.Calls);
        Assert.Equal(Roles.Teacher, accounts.LastRole);
    }

    [Fact]
    public async Task ClassFile_PresignWithoutAccountDirectory_IsRejected()
    {
        var fixture = new Fixture();
        fixture.User.Role = Roles.Teacher;
        await Fails(403, () => fixture.Service.PresignUploadAsync(new("exam.pdf", "application/pdf", 100, "material")));
    }

    [Fact]
    public async Task ClassFileAccess_ResolvesOnlyOwnersConfirmedFileOfTheRequestedPurpose()
    {
        var store = new MemoryFileStore();
        var owner = Guid.NewGuid();
        var other = Guid.NewGuid();
        StoredFile Make(Guid by, string purpose, bool confirmed)
        {
            var file = StoredFile.Create(by, "exam.pdf", "application/pdf", 10, purpose, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddMinutes(5));
            if (confirmed) file.Complete($"files/{file.Id:N}", DateTimeOffset.UnixEpoch);
            return file;
        }
        var good = Make(owner, "exam", true);
        var unconfirmed = Make(owner, "exam", false);
        var wrongPurpose = Make(owner, "submission", true);
        var avatar = Make(owner, "avatar", true);
        foreach (var file in new[] { good, unconfirmed, wrongPurpose, avatar }) await store.AddAsync(file, default);
        var services = new ServiceCollection();
        services.AddSingleton<IFileStore>(store);
        services.AddSingleton<IObjectStorage>(new MemoryObjectStorage());
        services.AddSingleton(TimeProvider.System);
        EnglishCenter.Modules.MediaNotification.Application.DependencyInjection.AddMediaNotificationApplication(services);
        using var provider = services.BuildServiceProvider();
        var access = provider.GetRequiredService<IClassFileAccess>();

        Assert.Equal(good.Id, (await access.GetConfirmedAsync(owner, good.Id, "exam", default))!.FileId);
        Assert.NotNull(await access.DownloadAsync(owner, good.Id, "exam", default));
        Assert.Null(await access.GetConfirmedAsync(other, good.Id, "exam", default));
        Assert.Null(await access.DownloadAsync(other, good.Id, "exam", default));
        Assert.Null(await access.GetConfirmedAsync(owner, good.Id, "material", default));
        Assert.Null(await access.GetConfirmedAsync(owner, unconfirmed.Id, "exam", default));
        // Chỉ "material"/"exam": file nộp bài và ảnh đại diện không bao giờ đi qua đường này.
        Assert.Null(await access.GetConfirmedAsync(owner, wrongPurpose.Id, "submission", default));
        Assert.Null(await access.DownloadAsync(owner, avatar.Id, "avatar", default));
        Assert.Null(await access.GetConfirmedAsync(owner, Guid.Empty, "exam", default));
    }
}
