using System.Collections.Concurrent;
using System.Reflection;
using System.Text;
using EnglishCenter.BuildingBlocks.Security;
using EnglishCenter.Modules.MediaNotification.Application.Files;
using EnglishCenter.Modules.MediaNotification.Domain.Files;
using Xunit;

namespace EnglishCenter.IntegrationTests;

// Application-service tests only: no fake is registered as a real database/S3 implementation.
public sealed partial class MediaFileServiceTests
{
    private static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.7\n1 0 obj\n<<>>\nendobj\n%%EOF\n");

    [Theory]
    [InlineData("lesson.pdf", "application/pdf")]
    [InlineData("lesson.docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document")]
    [InlineData("lesson.mp3", "audio/mpeg")]
    [InlineData("lesson.m4a", "audio/mp4")]
    [InlineData("lesson.jpg", "image/jpeg")]
    [InlineData("lesson.png", "image/png")]
    public async Task Presign_AllSixAllowedPairs_AcceptExactMaximum(string fileName, string contentType)
    {
        var fixture = new Fixture();

        var ticket = await fixture.Service.PresignUploadAsync(
            new PresignUploadRequest(fileName, contentType, FileService.MaxSizeBytes, "submission"));

        var saved = fixture.Store.Snapshot(ticket.FileId);
        Assert.Equal(fixture.User.UserId, saved.OwnerId);
        Assert.Equal(fileName, saved.FileName);
        Assert.Equal(contentType, saved.ContentType);
        Assert.Equal(FileService.MaxSizeBytes, saved.SizeBytes);
        Assert.Equal("submission", saved.Purpose);
        Assert.Null(saved.FileKey);
        Assert.Null(saved.UploadedAt);
        Assert.Equal(fixture.Clock.GetUtcNow(), saved.CreatedAt);
        Assert.Equal(saved.CreatedAt.AddMinutes(5), ticket.ExpiresAt);
        Assert.Equal("PUT", ticket.Method);
        Assert.Equal(contentType, ticket.RequiredHeaders["Content-Type"]);
        Assert.Equal(saved.StagingKey, ticket.FileKey);
        var call = Assert.Single(fixture.Storage.Calls);
        Assert.Equal("put", call.Operation);
        Assert.Equal(ticket.FileKey, call.Key);
        Assert.Equal(ticket.Url, fixture.Storage.SignedUri(ticket.FileKey));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(-1L)]
    [InlineData(0L)]
    [InlineData(20971521L)]
    [InlineData(long.MaxValue)]
    public async Task Presign_InvalidSize_RejectsBeforeAnyIo(long? size)
    {
        var fixture = new Fixture();

        await Fails(400, () => fixture.Service.PresignUploadAsync(Request() with { SizeBytes = size }));

        Assert.Equal(0, fixture.Store.Calls);
        Assert.Empty(fixture.Storage.Calls);
    }

    [Theory]
    [InlineData("../lesson.pdf", "application/pdf", "submission")]
    [InlineData("folder/lesson.pdf", "application/pdf", "submission")]
    [InlineData("folder\\lesson.pdf", "application/pdf", "submission")]
    [InlineData("https://evil.invalid/lesson.pdf", "application/pdf", "submission")]
    [InlineData("lesson.pdf.exe", "application/pdf", "submission")]
    [InlineData("lesson.pdf", "image/png", "submission")]
    [InlineData("lesson.png", "application/pdf", "submission")]
    [InlineData("lesson.pdf", "application/pdf", "avatar")]
    public async Task Presign_InvalidPairPurposeOrClientPath_RejectsBeforeAnyIo(
        string fileName, string contentType, string purpose)
    {
        var fixture = new Fixture();

        await Fails(400, () => fixture.Service.PresignUploadAsync(
            new PresignUploadRequest(fileName, contentType, Pdf.Length, purpose)));

        Assert.Equal(0, fixture.Store.Calls);
        Assert.Empty(fixture.Storage.Calls);
    }

    [Fact]
    public async Task Presign_GeneratesUniqueOwnerScopedStagingKeys_NotClientFileNames()
    {
        var fixture = new Fixture();

        var first = await fixture.Service.PresignUploadAsync(Request());
        var second = await fixture.Service.PresignUploadAsync(Request());

        Assert.NotEqual(first.FileId, second.FileId);
        Assert.NotEqual(first.FileKey, second.FileKey);
        foreach (var ticket in new[] { first, second })
        {
            Assert.StartsWith("staging/", ticket.FileKey);
            var owner = fixture.User.UserId!.Value;
            Assert.True(ticket.FileKey.Contains(owner.ToString("N"), StringComparison.OrdinalIgnoreCase) ||
                ticket.FileKey.Contains(owner.ToString("D"), StringComparison.OrdinalIgnoreCase),
                "The server-generated staging namespace must include the authenticated owner.");
            Assert.DoesNotContain("lesson.pdf", ticket.FileKey);
            Assert.Equal(fixture.Store.Snapshot(ticket.FileId).StagingKey, ticket.FileKey);
        }
    }

    [Theory]
    [InlineData(false, "22222222-2222-2222-2222-222222222222")]
    [InlineData(false, "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData(true, null)]
    [InlineData(true, "00000000-0000-0000-0000-000000000000")]
    public async Task EveryOperation_RequiresAuthenticatedNonEmptyOwner_BeforeIo(bool authenticated, string? subject)
    {
        var fixture = new Fixture();
        fixture.User.IsAuthenticated = authenticated;
        fixture.User.UserId = subject is null ? null : Guid.Parse(subject);

        await Fails(401, () => fixture.Service.PresignUploadAsync(Request()));
        await Fails(401, () => fixture.Service.ConfirmUploadAsync(Guid.NewGuid()));
        await Fails(401, () => fixture.Service.PresignDownloadAsync(Guid.NewGuid()));

        Assert.Equal(0, fixture.Store.Calls);
        Assert.Empty(fixture.Storage.Calls);
    }

    [Fact]
    public async Task Download_PendingUpload_Returns409WithoutStorageAccess()
    {
        var fixture = new Fixture();
        var ticket = await fixture.Upload();
        fixture.Storage.Calls.Clear();

        await Fails(409, () => fixture.Service.PresignDownloadAsync(ticket.FileId));

        Assert.Empty(fixture.Storage.Calls);
    }

    [Theory]
    [InlineData(Roles.Student, false)]
    [InlineData(Roles.Student, true)]
    [InlineData(Roles.Admin, false)]
    [InlineData(Roles.Admin, true)]
    public async Task ForeignAndMissingIds_ReturnSame404_EvenForAdmin_WithoutStorageAccess(string role, bool confirmed)
    {
        var fixture = new Fixture();
        var ticket = await fixture.Upload();
        if (confirmed) await fixture.Service.ConfirmUploadAsync(ticket.FileId);
        fixture.User.UserId = Guid.NewGuid();
        fixture.User.Role = role;
        fixture.Storage.Calls.Clear();

        foreach (var id in new[] { ticket.FileId, Guid.NewGuid() })
        {
            var confirm = await Fails(404, () => fixture.Service.ConfirmUploadAsync(id));
            var download = await Fails(404, () => fixture.Service.PresignDownloadAsync(id));
            Assert.Equal("File not found.", confirm.Message);
            Assert.Equal(confirm.Message, download.Message);
        }

        Assert.Empty(fixture.Storage.Calls);
        Assert.DoesNotContain(fixture.Store.Completions, x => x.OwnerId == fixture.User.UserId);
    }

    [Fact]
    public async Task Confirm_MissingObject_Returns409AndKeepsMetadataPending()
    {
        var fixture = new Fixture();
        var ticket = await fixture.Service.PresignUploadAsync(Request());
        fixture.Storage.Calls.Clear();

        await Fails(409, () => fixture.Service.ConfirmUploadAsync(ticket.FileId));

        Assert.Null(fixture.Store.Snapshot(ticket.FileId).UploadedAt);
        Assert.Equal("head", Assert.Single(fixture.Storage.Calls).Operation);
        Assert.Empty(fixture.Store.Completions);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Confirm_StagingSizeOrMimeMismatch_RejectsBeforeCopy(bool sizeMismatch)
    {
        var fixture = new Fixture();
        var ticket = await fixture.Upload();
        fixture.Storage.Put(ticket.FileKey, Pdf, sizeMismatch ? "application/pdf" : "image/png",
            declaredSize: sizeMismatch ? Pdf.Length + 1 : Pdf.Length);
        fixture.Storage.Calls.Clear();

        await Fails(400, () => fixture.Service.ConfirmUploadAsync(ticket.FileId));

        Assert.Equal("head", Assert.Single(fixture.Storage.Calls).Operation);
        Assert.Empty(fixture.Store.Completions);
        Assert.Null(fixture.Store.Snapshot(ticket.FileId).FileKey);
    }

    [Fact]
    public async Task Confirm_MissingStableEtag_RejectsBeforeCopy()
    {
        var fixture = new Fixture();
        var ticket = await fixture.Upload();
        fixture.Storage.Put(ticket.FileKey, Pdf, "application/pdf", etag: "");
        fixture.Storage.Calls.Clear();

        await Fails(409, () => fixture.Service.ConfirmUploadAsync(ticket.FileId));

        Assert.Equal("head", Assert.Single(fixture.Storage.Calls).Operation);
        Assert.Empty(fixture.Store.Completions);
    }

    [Fact]
    public async Task Confirm_InvalidContentDespiteMatchingMetadata_RejectsAndDeletesOnlyCandidate()
    {
        var fixture = new Fixture();
        var ticket = await fixture.Upload();
        fixture.Storage.Put(ticket.FileKey, Enumerable.Repeat((byte)'x', Pdf.Length).ToArray(), "application/pdf");

        await Fails(400, () => fixture.Service.ConfirmUploadAsync(ticket.FileId));

        var copy = Assert.Single(fixture.Storage.Copies);
        Assert.Equal(copy.Destination, Assert.Single(fixture.Storage.Deleted));
        Assert.False(fixture.Storage.Contains(copy.Destination));
        Assert.True(fixture.Storage.Contains(ticket.FileKey));
        Assert.Empty(fixture.Store.Completions);
        Assert.Equal(0, fixture.Store.CompletionAttempts);
        Assert.Null(fixture.Store.Snapshot(ticket.FileId).FileKey);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Confirm_FinalSizeOrMimeMismatch_RejectsBeforeCommitAndCleansCandidate(bool sizeMismatch)
    {
        var fixture = new Fixture();
        var ticket = await fixture.Upload();
        fixture.Storage.AfterCopy = copy => fixture.Storage.Put(copy.Destination, Pdf,
            sizeMismatch ? "application/pdf" : "image/png", declaredSize: sizeMismatch ? Pdf.Length + 1 : Pdf.Length);

        await Fails(400, () => fixture.Service.ConfirmUploadAsync(ticket.FileId));

        Assert.Empty(fixture.Store.Completions);
        Assert.Equal(Assert.Single(fixture.Storage.Copies).Destination, Assert.Single(fixture.Storage.Deleted));
        Assert.True(fixture.Storage.Contains(ticket.FileKey));
    }

    [Fact]
    public async Task Confirm_ExactMaximumValidPdf_SucceedsUsingActualBytes()
    {
        var fixture = new Fixture();
        var bytes = new byte[FileService.MaxSizeBytes];
        "%PDF-1.7\n"u8.CopyTo(bytes);
        "\n%%EOF\n"u8.CopyTo(bytes.AsSpan(bytes.Length - 7));
        var ticket = await fixture.Upload(bytes);

        var response = await fixture.Service.ConfirmUploadAsync(ticket.FileId);

        Assert.Equal(FileService.MaxSizeBytes, response.SizeBytes);
        Assert.Equal(bytes, fixture.Storage.Bytes(response.FileKey));
    }

    [Fact]
    public async Task Confirm_SucceedsByCopyingToPrivateUniqueKey_AndIssuesDownloadForFinalOnly()
    {
        var fixture = new Fixture();
        var ticket = await fixture.Upload();
        var originalRevision = fixture.Store.Snapshot(ticket.FileId).Revision;
        fixture.Clock.Advance(TimeSpan.FromSeconds(30));

        var response = await fixture.Service.ConfirmUploadAsync(ticket.FileId);
        var download = await fixture.Service.PresignDownloadAsync(ticket.FileId);

        Assert.Equal(ticket.FileId, response.FileId);
        Assert.NotEqual(ticket.FileKey, response.FileKey);
        Assert.StartsWith("files/", response.FileKey);
        Assert.Equal("lesson.pdf", response.FileName);
        Assert.Equal("application/pdf", response.ContentType);
        Assert.Equal(Pdf.Length, response.SizeBytes);
        Assert.Equal(fixture.Clock.GetUtcNow(), response.UploadedAt);
        Assert.Equal(Pdf, fixture.Storage.Bytes(response.FileKey));
        var saved = fixture.Store.Snapshot(ticket.FileId);
        Assert.Equal(response.FileKey, saved.FileKey);
        Assert.NotEqual(originalRevision, saved.Revision);
        var copy = Assert.Single(fixture.Storage.Copies);
        Assert.Equal(ticket.FileKey, copy.Source);
        Assert.Equal(response.FileKey, copy.Destination);
        Assert.False(string.IsNullOrWhiteSpace(copy.SourceETag));
        Assert.Equal("application/pdf", copy.ContentType);
        Assert.Equal(response.FileKey, Assert.Single(fixture.Storage.Calls, x => x.Operation == "get").Key);
        Assert.DoesNotContain(fixture.Storage.Calls, x => x.Operation == "put" && x.Key == response.FileKey);
        Assert.Equal(fixture.Storage.SignedUri(response.FileKey), download.Url);
        Assert.Equal(fixture.Clock.GetUtcNow().AddMinutes(5), download.ExpiresAt);
        Assert.Empty(fixture.Storage.Deleted);
    }

    [Fact]
    public async Task StagingPutReplay_CannotChangeAcceptedContentOrDownloadTarget()
    {
        var fixture = new Fixture();
        var ticket = await fixture.Upload();
        var accepted = await fixture.Service.ConfirmUploadAsync(ticket.FileId);
        fixture.Storage.Put(ticket.FileKey, Encoding.ASCII.GetBytes("attacker replacement"), "application/pdf");
        fixture.Clock.Advance(TimeSpan.FromMinutes(10));
        fixture.Storage.Calls.Clear();

        var repeated = await fixture.Service.ConfirmUploadAsync(ticket.FileId);
        var download = await fixture.Service.PresignDownloadAsync(ticket.FileId);

        Assert.Equal(accepted, repeated);
        Assert.Equal(Pdf, fixture.Storage.Bytes(accepted.FileKey));
        Assert.Equal(fixture.Storage.SignedUri(accepted.FileKey), download.Url);
        Assert.Equal("get", Assert.Single(fixture.Storage.Calls).Operation);
        Assert.Single(fixture.Storage.Copies);
        Assert.Single(fixture.Store.Completions);
    }

    [Fact]
    public async Task Confirm_ConcurrentAttempts_OneCasWinner_LoserDeletesOnlyItsOwnCandidate()
    {
        var fixture = new Fixture();
        var ticket = await fixture.Upload();
        var bothReachedCas = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var arrivals = 0;
        fixture.Store.BeforeComplete = async ct =>
        {
            if (Interlocked.Increment(ref arrivals) == 2) bothReachedCas.TrySetResult(true);
            await bothReachedCas.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
        };

        var responses = await Task.WhenAll(
            fixture.Service.ConfirmUploadAsync(ticket.FileId), fixture.Service.ConfirmUploadAsync(ticket.FileId));

        Assert.Equal(responses[0], responses[1]);
        Assert.Equal(2, fixture.Storage.Copies.Count);
        Assert.Equal(2, fixture.Storage.Copies.Select(x => x.Destination).Distinct().Count());
        Assert.Single(fixture.Store.Completions.Select(x => x.Revision).Distinct());
        var winner = Assert.Single(fixture.Store.Completions, x => x.Won);
        var loser = Assert.Single(fixture.Store.Completions, x => !x.Won);
        Assert.NotEqual(winner.FileKey, loser.FileKey);
        Assert.Equal(winner.FileKey, responses[0].FileKey);
        Assert.Equal(loser.FileKey, Assert.Single(fixture.Storage.Deleted));
        Assert.True(fixture.Storage.Contains(winner.FileKey));
        Assert.False(fixture.Storage.Contains(loser.FileKey));
        Assert.True(fixture.Storage.Contains(ticket.FileKey));
        Assert.Equal(winner.FileKey, fixture.Store.Snapshot(ticket.FileId).FileKey);
        Assert.Equal(2, fixture.Store.CompletionAttempts);
    }

    [Fact]
    public async Task Confirm_StagingChangedBetweenHeadAndCopy_RejectsConditionalCopy()
    {
        var fixture = new Fixture();
        var ticket = await fixture.Upload();
        fixture.Storage.BeforeCopy = (_, _) =>
        {
            fixture.Storage.Put(ticket.FileKey, Pdf, "application/pdf");
            return Task.CompletedTask;
        };

        await Fails(409, () => fixture.Service.ConfirmUploadAsync(ticket.FileId));

        Assert.Empty(fixture.Store.Completions);
        Assert.Null(fixture.Store.Snapshot(ticket.FileId).UploadedAt);
        Assert.True(fixture.Storage.Contains(ticket.FileKey));
        Assert.Equal(Assert.Single(fixture.Storage.Copies).Destination, Assert.Single(fixture.Storage.Deleted));
    }

    [Fact]
    public async Task Confirm_ExpiryAtBoundary_RejectsBeforeStorage()
    {
        var fixture = new Fixture();
        var ticket = await fixture.Upload();
        fixture.Clock.Advance(ticket.ExpiresAt - fixture.Clock.GetUtcNow());
        fixture.Storage.Calls.Clear();

        await Fails(409, () => fixture.Service.ConfirmUploadAsync(ticket.FileId));

        Assert.Empty(fixture.Storage.Calls);
        Assert.Empty(fixture.Store.Completions);
    }

    [Fact]
    public async Task Confirm_ExpiryDuringValidation_DoesNotCommitAndCleansCandidate()
    {
        var fixture = new Fixture();
        var ticket = await fixture.Upload();
        fixture.Storage.AfterRead = () => fixture.Clock.Advance(TimeSpan.FromMinutes(5));

        await Fails(409, () => fixture.Service.ConfirmUploadAsync(ticket.FileId));

        Assert.Empty(fixture.Store.Completions);
        Assert.Equal(Assert.Single(fixture.Storage.Copies).Destination, Assert.Single(fixture.Storage.Deleted));
    }

    [Fact]
    public async Task Confirm_LostDatabaseAcknowledgement_DoesNotDeleteReferencedWinner()
    {
        var fixture = new Fixture();
        var ticket = await fixture.Upload();
        fixture.Store.ThrowAfterCommit = true;

        await Fails(503, () => fixture.Service.ConfirmUploadAsync(ticket.FileId));

        var saved = fixture.Store.Snapshot(ticket.FileId);
        Assert.NotNull(saved.UploadedAt);
        Assert.NotNull(saved.FileKey);
        Assert.True(fixture.Storage.Contains(saved.FileKey!));
        Assert.Empty(fixture.Storage.Deleted);
        fixture.Store.ThrowAfterCommit = false;
        var retry = await fixture.Service.ConfirmUploadAsync(ticket.FileId);
        Assert.Equal(saved.FileKey, retry.FileKey);
        Assert.Single(fixture.Storage.Copies);
    }

    [Fact]
    public async Task Confirm_UnknownCompletionOutcomeWithDelayedCommit_RetainsCandidateUntilCommitBecomesVisible()
    {
        var fixture = new Fixture();
        var ticket = await fixture.Upload();
        var storageFailure = new FileFailure(503, "File storage is temporarily unavailable.");
        fixture.Store.FailBeforeCommitVisible = _ => storageFailure;

        var failure = await Fails(503, () => fixture.Service.ConfirmUploadAsync(ticket.FileId));

        Assert.Same(storageFailure, failure);
        Assert.Equal(1, fixture.Store.CompletionAttempts);
        var candidate = Assert.Single(fixture.Storage.Copies).Destination;
        // Ordinary MVCC reads cannot see this transaction yet. The fake does not invent a
        // positive IsReferenced result: it reads only committed metadata, just like SQL.
        Assert.Null(fixture.Store.Snapshot(ticket.FileId).FileKey);
        fixture.Store.FinishDeferredCommit();

        Assert.Equal(candidate, fixture.Store.Snapshot(ticket.FileId).FileKey);
        Assert.True(fixture.Storage.Contains(candidate),
            "An outcome-unknown completion can commit later; its accepted object must not have been deleted.");
        Assert.Equal(Pdf, fixture.Storage.Bytes(candidate));
        Assert.Empty(fixture.Storage.Deleted);
        Assert.Empty(fixture.Store.ReferenceChecks);
    }

    [Fact]
    public async Task Confirm_CancellationWhileCompletionAttempted_RetainsCandidateForDelayedCommit()
    {
        var fixture = new Fixture();
        var ticket = await fixture.Upload();
        using var cancellation = new CancellationTokenSource();
        fixture.Store.FailBeforeCommitVisible = ct =>
        {
            cancellation.Cancel();
            return new OperationCanceledException(ct);
        };

        var failure = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => fixture.Service.ConfirmUploadAsync(ticket.FileId, cancellation.Token));

        Assert.Equal(cancellation.Token, failure.CancellationToken);
        Assert.True(cancellation.IsCancellationRequested);
        Assert.Equal(1, fixture.Store.CompletionAttempts);
        var candidate = Assert.Single(fixture.Storage.Copies).Destination;
        Assert.Null(fixture.Store.Snapshot(ticket.FileId).FileKey);
        fixture.Store.FinishDeferredCommit();

        Assert.Equal(candidate, fixture.Store.Snapshot(ticket.FileId).FileKey);
        Assert.True(fixture.Storage.Contains(candidate),
            "Cancellation after persistence starts does not prove rollback; a late commit must retain its object.");
        Assert.Equal(Pdf, fixture.Storage.Bytes(candidate));
        Assert.Empty(fixture.Storage.Deleted);
        Assert.Empty(fixture.Store.ReferenceChecks);
    }

    [Theory]
    [InlineData("upload")]
    [InlineData("confirm")]
    [InlineData("download")]
    public async Task StorageBoundaryGeneric503_PropagatesWithoutLeakingDetails(string operation)
    {
        var fixture = new Fixture();
        var ticket = await fixture.Upload();
        if (operation == "download") await fixture.Service.ConfirmUploadAsync(ticket.FileId);
        // Infrastructure owns translation of SDK errors. This tests the application contract,
        // not S3's real exception translation or a manufactured real-storage success.
        var failure = new FileFailure(503, "File storage is temporarily unavailable.");
        fixture.Storage.Failure = _ => failure;

        var actual = await Fails(503, () => fixture.Invoke(operation, ticket.FileId));

        Assert.Same(failure, actual);
        Assert.Equal("File storage is temporarily unavailable.", actual.Message);
    }

    [Theory]
    [InlineData("upload")]
    [InlineData("confirm")]
    [InlineData("download")]
    public async Task Cancellation_PropagatesOriginalToken(string operation)
    {
        var fixture = new Fixture();
        var ticket = await fixture.Upload();
        if (operation == "download") await fixture.Service.ConfirmUploadAsync(ticket.FileId);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var failure = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => fixture.Invoke(operation, ticket.FileId, cancellation.Token));

        Assert.Equal(cancellation.Token, failure.CancellationToken);
    }

    [Fact]
    public async Task Confirm_CancellationDuringCopy_PropagatesAndCleansWithIndependentToken()
    {
        var fixture = new Fixture();
        var ticket = await fixture.Upload();
        using var cancellation = new CancellationTokenSource();
        fixture.Storage.BeforeCopy = (_, ct) =>
        {
            cancellation.Cancel();
            ct.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        };

        var failure = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => fixture.Service.ConfirmUploadAsync(ticket.FileId, cancellation.Token));

        Assert.Equal(cancellation.Token, failure.CancellationToken);
        Assert.Empty(fixture.Store.Completions);
        Assert.Null(fixture.Store.Snapshot(ticket.FileId).UploadedAt);
        var deletion = Assert.Single(fixture.Storage.Calls, x => x.Operation == "delete");
        Assert.NotEqual(cancellation.Token, deletion.Token);
        Assert.False(deletion.Token.IsCancellationRequested);
        Assert.Equal(Assert.Single(fixture.Storage.Copies).Destination, deletion.Key);
        Assert.True(fixture.Storage.Contains(ticket.FileKey));
    }

    private static PresignUploadRequest Request() => new("lesson.pdf", "application/pdf", Pdf.Length, "submission");

    private static async Task<FileFailure> Fails(int status, Func<Task> action)
    {
        var failure = await Assert.ThrowsAsync<FileFailure>(action);
        Assert.Equal(status, failure.Status);
        return failure;
    }

    private sealed class Fixture
    {
        public MutableCurrentUser User { get; } = new();
        public ManualClock Clock { get; } = new();
        public MemoryFileStore Store { get; } = new();
        public MemoryObjectStorage Storage { get; } = new();
        public FileService Service { get; }

        public Fixture(EnglishCenter.Modules.Identity.Contracts.IAccountAccess? accounts = null) =>
            Service = new FileService(Store, Storage, User, Clock, accounts);

        public async Task<UploadTicket> Upload(byte[]? content = null)
        {
            var bytes = content ?? Pdf;
            var ticket = await Service.PresignUploadAsync(Request() with { SizeBytes = bytes.Length });
            Storage.Put(ticket.FileKey, bytes, "application/pdf");
            return ticket;
        }

        public Task Invoke(string operation, Guid id, CancellationToken ct = default) => operation switch
        {
            "upload" => Service.PresignUploadAsync(Request(), ct),
            "confirm" => Service.ConfirmUploadAsync(id, ct),
            "download" => Service.PresignDownloadAsync(id, ct),
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };
    }

    private sealed class MutableCurrentUser : ICurrentUser
    {
        public bool IsAuthenticated { get; set; } = true;
        public Guid? UserId { get; set; } = Guid.NewGuid();
        public string? Role { get; set; } = Roles.Student;
        public bool IsInRole(string role) => Role == role;
    }

    private sealed class ManualClock : TimeProvider
    {
        private long ticks = new DateTimeOffset(2026, 11, 7, 12, 0, 0, TimeSpan.Zero).Ticks;
        public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref ticks), TimeSpan.Zero);
        public void Advance(TimeSpan value) => Interlocked.Add(ref ticks, value.Ticks);
    }

    private sealed record Completion(Guid OwnerId, Guid Revision, string FileKey, bool Won);
    private sealed record ReferenceCheck(string FileKey, bool Referenced);
    private sealed record DeferredCompletion(Guid FileId, Guid OwnerId, Guid Revision, string FileKey,
        DateTimeOffset UploadedAt);

    private sealed class MemoryFileStore : IFileStore
    {
        private readonly object gate = new();
        private readonly Dictionary<Guid, StoredFile> files = [];
        private int calls;
        private int completionAttempts;
        private DeferredCompletion? deferredCompletion;
        public int Calls => Volatile.Read(ref calls);
        public int CompletionAttempts => Volatile.Read(ref completionAttempts);
        public ConcurrentQueue<Completion> Completions { get; } = new();
        public ConcurrentQueue<ReferenceCheck> ReferenceChecks { get; } = new();
        public Func<CancellationToken, Task>? BeforeComplete { get; set; }
        public bool ThrowAfterCommit { get; set; }
        public Func<CancellationToken, Exception>? FailBeforeCommitVisible { get; set; }

        // Every field is a value/string. Clone to emulate AsNoTracking: sharing the entity
        // between requests would mask stale revisions and make the concurrency test invalid.
        private static readonly MethodInfo CloneMethod = typeof(object).GetMethod("MemberwiseClone",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        private static StoredFile Clone(StoredFile file) => (StoredFile)CloneMethod.Invoke(file, null)!;

        private void Enter(CancellationToken ct)
        {
            Interlocked.Increment(ref calls);
            ct.ThrowIfCancellationRequested();
        }

        public StoredFile Snapshot(Guid id)
        {
            lock (gate) return Clone(files[id]);
        }

        public Task AddAsync(StoredFile file, CancellationToken ct)
        {
            Enter(ct);
            lock (gate) files.Add(file.Id, Clone(file));
            return Task.CompletedTask;
        }

        public Task<StoredFile?> FindOwnedAsync(Guid fileId, Guid ownerId, CancellationToken ct)
        {
            Enter(ct);
            lock (gate)
                return Task.FromResult(files.TryGetValue(fileId, out var file) && file.OwnerId == ownerId
                    ? Clone(file) : null);
        }

        public async Task<bool> TryCompleteAsync(Guid fileId, Guid ownerId, Guid revision, string fileKey,
            DateTimeOffset uploadedAt, CancellationToken ct)
        {
            Enter(ct);
            Interlocked.Increment(ref completionAttempts);
            if (BeforeComplete is not null) await BeforeComplete(ct);
            ct.ThrowIfCancellationRequested();
            lock (gate)
            {
                var won = files.TryGetValue(fileId, out var file) && file.OwnerId == ownerId &&
                    file.Revision == revision && file.UploadedAt is null;
                if (won && FailBeforeCommitVisible is { } fail)
                {
                    if (deferredCompletion is not null)
                        throw new InvalidOperationException("Only one delayed transaction is supported by this fake.");
                    deferredCompletion = new DeferredCompletion(fileId, ownerId, revision, fileKey, uploadedAt);
                    // Simulate the client losing the outcome before the server transaction commits.
                    // Leave committed rows unchanged so reference checks return a genuine false.
                    throw fail(ct);
                }
                if (won) file!.Complete(fileKey, uploadedAt);
                Completions.Enqueue(new Completion(ownerId, revision, fileKey, won));
                if (won && ThrowAfterCommit) throw new FileFailure(503, "File storage is temporarily unavailable.");
                return won;
            }
        }

        public void FinishDeferredCommit()
        {
            lock (gate)
            {
                var pending = deferredCompletion ?? throw new InvalidOperationException("No delayed transaction exists.");
                var file = files[pending.FileId];
                if (file.OwnerId != pending.OwnerId || file.Revision != pending.Revision || file.UploadedAt is not null)
                    throw new InvalidOperationException("Delayed transaction no longer matches its pending row.");
                file.Complete(pending.FileKey, pending.UploadedAt);
                Completions.Enqueue(new Completion(pending.OwnerId, pending.Revision, pending.FileKey, true));
                deferredCompletion = null;
            }
        }

        public Task<bool> IsReferencedAsync(string fileKey, CancellationToken ct)
        {
            Enter(ct);
            lock (gate)
            {
                var referenced = files.Values.Any(x => x.FileKey == fileKey);
                ReferenceChecks.Enqueue(new ReferenceCheck(fileKey, referenced));
                return Task.FromResult(referenced);
            }
        }
    }

    private sealed record StorageCall(string Operation, string Key, CancellationToken Token);
    private sealed record CopyAttempt(string Source, string Destination, string SourceETag, string ContentType);

    private sealed class MemoryObjectStorage : IObjectStorage
    {
        private sealed record Blob(byte[] Content, string ContentType, long Size, string ETag);
        private readonly object gate = new();
        private readonly Dictionary<string, Blob> objects = new(StringComparer.Ordinal);
        public ConcurrentQueue<StorageCall> Calls { get; } = new();
        public ConcurrentQueue<CopyAttempt> Copies { get; } = new();
        public ConcurrentQueue<string> Deleted { get; } = new();
        public Func<string, Exception?>? Failure { get; set; }
        public Func<CopyAttempt, CancellationToken, Task>? BeforeCopy { get; set; }
        public Action<CopyAttempt>? AfterCopy { get; set; }
        public Action? AfterRead { get; set; }

        public Uri SignedUri(string key) => new($"https://storage.invalid/private/{key}");

        public void Put(string key, byte[] content, string contentType, long? declaredSize = null, string? etag = null)
        {
            lock (gate) objects[key] = new Blob(content.ToArray(), contentType,
                declaredSize ?? content.LongLength, etag ?? Guid.NewGuid().ToString("N"));
        }

        public bool Contains(string key)
        {
            lock (gate) return objects.ContainsKey(key);
        }

        public byte[] Bytes(string key)
        {
            lock (gate) return objects[key].Content.ToArray();
        }

        private void Enter(string operation, string key, CancellationToken ct)
        {
            Calls.Enqueue(new StorageCall(operation, key, ct));
            ct.ThrowIfCancellationRequested();
            if (Failure?.Invoke(operation) is { } failure) throw failure;
        }

        public Task<Uri> PresignPutAsync(string key, string contentType, DateTimeOffset expiresAt, CancellationToken ct)
        {
            Enter("put", key, ct);
            return Task.FromResult(SignedUri(key));
        }

        public Task<Uri> PresignGetAsync(string key, string fileName, DateTimeOffset expiresAt, CancellationToken ct)
        {
            Enter("get", key, ct);
            return Task.FromResult(SignedUri(key));
        }

        public Task<ObjectInfo?> HeadAsync(string key, CancellationToken ct)
        {
            Enter("head", key, ct);
            lock (gate) return Task.FromResult(objects.TryGetValue(key, out var blob)
                ? new ObjectInfo(blob.Size, blob.ContentType, blob.ETag) : null);
        }

        public async Task CopyAsync(string sourceKey, string destinationKey, string sourceETag,
            string contentType, CancellationToken ct)
        {
            Enter("copy", sourceKey, ct);
            var attempt = new CopyAttempt(sourceKey, destinationKey, sourceETag, contentType);
            Copies.Enqueue(attempt);
            if (BeforeCopy is not null) await BeforeCopy(attempt, ct);
            ct.ThrowIfCancellationRequested();
            lock (gate)
            {
                if (!objects.TryGetValue(sourceKey, out var source) || source.ETag != sourceETag)
                    throw new FileFailure(409, "Upload changed during confirmation. Retry confirmation.");
                if (objects.ContainsKey(destinationKey)) throw new InvalidOperationException("Candidate keys must be unique.");
                objects.Add(destinationKey, new Blob(source.Content.ToArray(), contentType,
                    source.Size, Guid.NewGuid().ToString("N")));
            }
            AfterCopy?.Invoke(attempt);
        }

        public Task<byte[]> ReadAsync(string key, int maxBytes, CancellationToken ct)
        {
            Enter("read", key, ct);
            var bytes = Bytes(key);
            if (bytes.Length > maxBytes) throw new FileFailure(400, "File exceeds the maximum allowed size.");
            AfterRead?.Invoke();
            return Task.FromResult(bytes);
        }

        public Task DeleteAsync(string key, CancellationToken ct)
        {
            Enter("delete", key, ct);
            lock (gate) objects.Remove(key);
            Deleted.Enqueue(key);
            return Task.CompletedTask;
        }
    }
}
