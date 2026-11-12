using EnglishCenter.Modules.Identity.Contracts;
using EnglishCenter.Modules.MediaNotification.Application.Files;
using EnglishCenter.Modules.MediaNotification.Domain.Files;
using Xunit;

namespace EnglishCenter.IntegrationTests;

public sealed partial class MediaFileServiceTests
{
    private const int AvatarMaximum = 2 * 1024 * 1024;

    [Theory]
    [InlineData("avatar.png", "image/png")]
    [InlineData("avatar.jpg", "image/jpeg")]
    public async Task Avatar_PresignAndConfirm_AcceptExactTwoMiB_AndImmutableFinalDownload(string name, string mime)
    {
        var accounts = new AvatarAccounts();
        var fixture = new Fixture(accounts);
        var bytes = AvatarBytes(name, AvatarMaximum);
        var ticket = await fixture.Service.PresignUploadAsync(new(name, mime, bytes.Length, "avatar"));
        fixture.Storage.Put(ticket.FileKey, bytes, mime);

        var accepted = await fixture.Service.ConfirmUploadAsync(ticket.FileId);
        fixture.Storage.Put(ticket.FileKey, new byte[AvatarMaximum], mime);
        var repeated = await fixture.Service.ConfirmUploadAsync(ticket.FileId);
        var download = await fixture.Service.PresignDownloadAsync(ticket.FileId);

        Assert.Equal(AvatarMaximum, accepted.SizeBytes);
        Assert.Equal(accepted, repeated);
        Assert.Equal("avatar", fixture.Store.Snapshot(ticket.FileId).Purpose);
        Assert.Equal(bytes, fixture.Storage.Bytes(accepted.FileKey));
        Assert.Equal(fixture.Storage.SignedUri(accepted.FileKey), download.Url);
        Assert.Equal(4, accounts.Calls);
        Assert.Equal(fixture.User.UserId, accounts.LastUserId);
        Assert.Equal(fixture.User.Role, accounts.LastRole);
    }

    [Theory]
    [InlineData("avatar.png", "image/png", 0L)]
    [InlineData("avatar.jpg", "image/jpeg", 2097153L)]
    [InlineData("avatar.png", "image/jpeg", 100L)]
    [InlineData("avatar.jpg", "image/png", 100L)]
    [InlineData("avatar.gif", "image/gif", 100L)]
    [InlineData("avatar.svg", "image/svg+xml", 100L)]
    [InlineData("avatar.pdf", "application/pdf", 100L)]
    public async Task Avatar_PresignRejectsInvalidPolicyBeforePersistenceOrStorage(string name, string mime, long size)
    {
        var fixture = new Fixture(new AvatarAccounts());
        await Fails(400, () => fixture.Service.PresignUploadAsync(new(name, mime, size, "avatar")));
        Assert.Equal(0, fixture.Store.Calls);
        Assert.Empty(fixture.Storage.Calls);
    }

    [Theory]
    [InlineData("avatar.png", "image/png")]
    [InlineData("avatar.jpg", "image/jpeg")]
    public async Task Avatar_ConfirmRejectsWrongSignature_DespiteMatchingMimeAndSize(string name, string mime)
    {
        var fixture = new Fixture(new AvatarAccounts());
        var bytes = Enumerable.Repeat((byte)'x', 100).ToArray();
        var ticket = await fixture.Service.PresignUploadAsync(new(name, mime, bytes.Length, "avatar"));
        fixture.Storage.Put(ticket.FileKey, bytes, mime);
        await Fails(400, () => fixture.Service.ConfirmUploadAsync(ticket.FileId));
        Assert.Null(fixture.Store.Snapshot(ticket.FileId).UploadedAt);
        Assert.Empty(fixture.Store.Completions);
        Assert.Equal(Assert.Single(fixture.Storage.Copies).Destination, Assert.Single(fixture.Storage.Deleted));
        Assert.True(fixture.Storage.Contains(ticket.FileKey));
    }

    [Theory]
    [InlineData("avatar.png", "image/png", 2097153L)]
    [InlineData("avatar.pdf", "application/pdf", 100L)]
    public async Task Avatar_ConfirmRevalidatesPersistedPolicy_BeforeStorage(string name, string mime, long size)
    {
        var fixture = new Fixture(new AvatarAccounts());
        var file = StoredFile.Create(fixture.User.UserId!.Value, name, mime, size, "avatar", fixture.Clock.GetUtcNow(), fixture.Clock.GetUtcNow().AddMinutes(5));
        await fixture.Store.AddAsync(file, default);
        await Fails(400, () => fixture.Service.ConfirmUploadAsync(file.Id));
        Assert.Empty(fixture.Storage.Calls);
        Assert.Empty(fixture.Store.Completions);
    }

    [Theory]
    [InlineData("presign")]
    [InlineData("confirm")]
    [InlineData("download")]
    public async Task Avatar_RevokedAccount_IsRejectedOnEveryOperation(string operation)
    {
        var accounts = new AvatarAccounts();
        var fixture = new Fixture(accounts);
        var bytes = AvatarBytes("avatar.jpg", 100);
        var request = new PresignUploadRequest("avatar.jpg", "image/jpeg", bytes.Length, "avatar");
        var ticket = await fixture.Service.PresignUploadAsync(request);
        fixture.Storage.Put(ticket.FileKey, bytes, "image/jpeg");
        if (operation == "download") await fixture.Service.ConfirmUploadAsync(ticket.FileId);
        accounts.Active = false;
        fixture.Storage.Calls.Clear();

        await Fails(403, () => operation switch
        {
            "presign" => fixture.Service.PresignUploadAsync(request),
            "confirm" => fixture.Service.ConfirmUploadAsync(ticket.FileId),
            _ => fixture.Service.PresignDownloadAsync(ticket.FileId)
        });
        Assert.Empty(fixture.Storage.Calls);
    }

    private static byte[] AvatarBytes(string name, int size)
    {
        var bytes = new byte[size];
        if (name.EndsWith(".png", StringComparison.Ordinal))
        {
            new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }.CopyTo(bytes, 0);
            "IHDR"u8.CopyTo(bytes.AsSpan(12));
        }
        else
        {
            bytes[0] = 0xff;
            bytes[1] = 0xd8;
            bytes[2] = 0xff;
            bytes[^2] = 0xff;
            bytes[^1] = 0xd9;
        }
        return bytes;
    }

    private sealed class AvatarAccounts : IAccountAccess
    {
        public bool Active { get; set; } = true;
        public int Calls { get; private set; }
        public Guid LastUserId { get; private set; }
        public string? LastRole { get; private set; }
        public Task<bool> IsActiveInRoleAsync(Guid userId, string? expectedRole, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Calls++;
            LastUserId = userId;
            LastRole = expectedRole;
            return Task.FromResult(Active);
        }
    }
}
