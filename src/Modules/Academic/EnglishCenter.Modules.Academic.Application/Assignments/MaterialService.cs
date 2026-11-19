using EnglishCenter.Modules.Academic.Domain.Assignments;
using EnglishCenter.Modules.MediaNotification.Contracts;

namespace EnglishCenter.Modules.Academic.Application.Assignments;

public sealed record CreateMaterialRequest(Guid ClassId, string? Title, string? Description, Guid? FileId);
public sealed record MaterialResponse(Guid Id, Guid ClassId, string Title, string Description, string FileName, string ContentType,
    long SizeBytes, Guid UploadedBy, DateTimeOffset CreatedAt);

public interface IMaterialStore
{
    Task<ClassMaterial?> FindAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<ClassMaterial>> ListAsync(Guid classId, CancellationToken ct);
    void Add(ClassMaterial item);
    Task SaveAsync(CancellationToken ct);
}

/// <summary>
/// Tài liệu độc lập của lớp. Đăng: Staff/Admin/Teacher đúng lớp, chỉ với file đã xác nhận do chính người đăng tải lên cho mục đích
/// "material". Xem/tải: bất kỳ ai còn quyền đọc lớp. Mỗi request kiểm tra lại quyền lớp hiện tại.
/// </summary>
public sealed class MaterialService(IMaterialStore store, CourseworkAccess access, IClassFileAccess media, TimeProvider clock)
{
    private const string Purpose = "material";
    private const int ListLimit = 200;

    public async Task<MaterialResponse> CreateAsync(CreateMaterialRequest request, CancellationToken ct)
    {
        await access.AuthorizeAsync(request.ClassId, CourseworkClassAccess.Manage, ct);
        var userId = access.UserId();
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Trim().Length > ClassMaterial.TitleMaxLength)
            errors["title"] = [$"Tiêu đề bắt buộc, tối đa {ClassMaterial.TitleMaxLength} ký tự."];
        if ((request.Description?.Trim().Length ?? 0) > ClassMaterial.DescriptionMaxLength)
            errors["description"] = [$"Mô tả tối đa {ClassMaterial.DescriptionMaxLength} ký tự."];
        if (request.FileId is null || request.FileId == Guid.Empty) errors["fileId"] = ["Chọn file tài liệu đã tải lên."];
        if (errors.Count > 0) throw new AcademicFailure(400, "Invalid material.", errors);

        var file = await media.GetConfirmedAsync(userId, request.FileId!.Value, Purpose, ct)
            ?? throw new AcademicFailure(400, "The file must be confirmed, owned by you and uploaded as a class material.",
                AssignmentService.Errors("fileId", "File chưa xác nhận, không phải của bạn hoặc không dùng cho tài liệu lớp."));
        var item = ClassMaterial.Create(request.ClassId, request.Title!, request.Description, file.FileId, file.FileName,
            file.ContentType, file.SizeBytes, userId, clock.GetUtcNow());
        store.Add(item);
        await store.SaveAsync(ct);
        return Response(item);
    }

    public async Task<IReadOnlyList<MaterialResponse>> ListAsync(Guid classId, CancellationToken ct)
    {
        await access.AuthorizeAsync(classId, CourseworkClassAccess.Read, ct);
        return (await store.ListAsync(classId, ct)).Take(ListLimit).Select(Response).ToList();
    }

    public async Task<FileDownloadResponse> DownloadAsync(Guid id, CancellationToken ct)
    {
        var item = await store.FindAsync(id, ct) ?? throw new AcademicFailure(404, "Material not found.");
        await access.AuthorizeAsync(item.ClassId, CourseworkClassAccess.Read, ct);
        var download = await media.DownloadAsync(item.UploadedBy, item.FileId, Purpose, ct)
            ?? throw new AcademicFailure(404, "Confirmed file not found.");
        return new(download.Url, download.ExpiresAt);
    }

    private static MaterialResponse Response(ClassMaterial x) => new(x.Id, x.ClassId, x.Title, x.Description, x.FileName,
        x.ContentType, x.SizeBytes, x.UploadedBy, x.CreatedAt);
}
