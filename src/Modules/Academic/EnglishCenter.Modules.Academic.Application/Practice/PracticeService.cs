using EnglishCenter.BuildingBlocks.Security;
using EnglishCenter.Modules.Academic.Application.Abstractions;
using EnglishCenter.Modules.Academic.Application.Assignments;
using EnglishCenter.Modules.Academic.Domain.Practice;

namespace EnglishCenter.Modules.Academic.Application.Practice;

public sealed record CreatePracticeRequest(Guid ClassId, string Title, string Content);
public sealed record UpdatePracticeRequest(string Title, string Content, int? ExpectedVersion);
public sealed record PracticeSummaryResponse(Guid Id, Guid ClassId, string Title, string Preview, Guid CreatedBy, string CreatedByName,
    DateTimeOffset CreatedAt, DateTimeOffset? UpdatedAt, int Version);
public sealed record PracticeResponse(Guid Id, Guid ClassId, string Title, string Content, Guid CreatedBy, string CreatedByName,
    DateTimeOffset CreatedAt, Guid? UpdatedBy, DateTimeOffset? UpdatedAt, int Version);

public interface IPracticeStore
{
    Task<PracticeExercise?> FindAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<PracticeExercise>> ListAsync(Guid classId, CancellationToken ct);
    void Add(PracticeExercise item);
    Task SaveAsync(CancellationToken ct);
}

/// <summary>
/// US-32 / Q-03: luyện tập nội dung văn bản theo lớp. Teacher đúng lớp tạo/sửa/xóa bài lớp mình, Staff/Admin quản lý mọi lớp,
/// Student còn quyền lớp chỉ xem danh sách/chi tiết. Không có quiz, chấm tự động hay nộp bài.
/// Nội dung là văn bản thuần; frontend phải hiển thị dạng text (escape), không render HTML.
/// </summary>
public sealed class PracticeService(IPracticeStore store, IAcademicStore classes, CourseworkAccess access, TimeProvider clock)
{
    private const int PreviewLength = 200;

    public async Task<PageResponse<PracticeSummaryResponse>> ListAsync(Guid classId, string? search, int page, int pageSize, CancellationToken ct)
    {
        if (classId == Guid.Empty) throw new AcademicFailure(400, "Class is required.", AssignmentService.Errors("classId", "Chọn lớp."));
        await access.AuthorizeAsync(classId, ClassAccess.Read, ct);
        var items = (await store.ListAsync(classId, ct))
            .Where(x => !x.IsDeleted && CourseworkAccess.Matches(search, x.Title, x.Content))
            .OrderByDescending(x => x.CreatedAt.UtcTicks).ThenBy(x => x.Id).ToList();
        var paged = CourseworkAccess.Page(items, page, pageSize);
        var names = await NamesAsync(paged.Items, ct);
        return new(paged.Items.Select(x => new PracticeSummaryResponse(x.Id, x.ClassId, x.Title, Preview(x.Content), x.CreatedBy,
            names[x.CreatedBy], x.CreatedAt, x.UpdatedAt, x.Version)).ToList(), paged.Total, paged.Page, paged.PageSize);
    }

    public async Task<PracticeResponse> GetAsync(Guid id, CancellationToken ct)
    {
        var item = await FindAsync(id, ct);
        await access.AuthorizeAsync(item.ClassId, ClassAccess.Read, ct);
        return await ResponseAsync(item, ct);
    }

    public async Task<PracticeResponse> CreateAsync(CreatePracticeRequest request, CancellationToken ct)
    {
        await access.AuthorizeAsync(request.ClassId, ClassAccess.Manage, ct);
        Validate(request.Title, request.Content);
        var item = PracticeExercise.Create(request.ClassId, request.Title, request.Content, access.UserId(), access.Role!, clock.GetUtcNow());
        store.Add(item);
        await store.SaveAsync(ct);
        return await ResponseAsync(item, ct);
    }

    public async Task<PracticeResponse> UpdateAsync(Guid id, UpdatePracticeRequest request, CancellationToken ct)
    {
        var item = await FindAsync(id, ct);
        await access.AuthorizeAsync(item.ClassId, ClassAccess.Manage, ct);
        Validate(request.Title, request.Content);
        if (request.ExpectedVersion is null)
            throw new AcademicFailure(400, "expectedVersion is required.", AssignmentService.Errors("expectedVersion", "Gửi version đang xem."));
        // Retry cùng nội dung sau khi mất response: trả bản hiện tại.
        if (request.ExpectedVersion + 1 == item.Version && item.Title == request.Title.Trim() && item.Content == request.Content.Trim() &&
            item.UpdatedBy == access.UserId())
            return await ResponseAsync(item, ct);
        if (request.ExpectedVersion != item.Version) throw new AcademicFailure(409, "Practice exercise changed. Reload before editing.");
        item.Update(request.Title, request.Content, access.UserId(), clock.GetUtcNow());
        await store.SaveAsync(ct);
        return await ResponseAsync(item, ct);
    }

    public async Task DeleteAsync(Guid id, int? expectedVersion, CancellationToken ct)
    {
        var item = await store.FindAsync(id, ct) ?? throw new AcademicFailure(404, "Practice exercise not found.");
        await access.AuthorizeAsync(item.ClassId, ClassAccess.Manage, ct);
        if (item.IsDeleted) return;
        if (expectedVersion is { } expected && expected != item.Version)
            throw new AcademicFailure(409, "Practice exercise changed. Reload before deleting.");
        item.Delete(access.UserId(), clock.GetUtcNow());
        await store.SaveAsync(ct);
    }

    private async Task<PracticeExercise> FindAsync(Guid id, CancellationToken ct)
    {
        var item = await store.FindAsync(id, ct);
        return item is null || item.IsDeleted ? throw new AcademicFailure(404, "Practice exercise not found.") : item;
    }

    private static void Validate(string? title, string? content)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(title) || title.Trim().Length > PracticeExercise.TitleMaxLength)
            errors["title"] = [$"Tên bài bắt buộc, tối đa {PracticeExercise.TitleMaxLength} ký tự."];
        if (string.IsNullOrWhiteSpace(content) || content.Trim().Length > PracticeExercise.ContentMaxLength)
            errors["content"] = [$"Nội dung bắt buộc, tối đa {PracticeExercise.ContentMaxLength} ký tự."];
        if (errors.Count > 0) throw new AcademicFailure(400, "Invalid practice exercise.", errors);
    }

    private async Task<PracticeResponse> ResponseAsync(PracticeExercise x, CancellationToken ct)
    {
        var names = await NamesAsync([x], ct);
        return new(x.Id, x.ClassId, x.Title, x.Content, x.CreatedBy, names[x.CreatedBy], x.CreatedAt, x.UpdatedBy, x.UpdatedAt, x.Version);
    }

    // Tên người tạo: giáo viên lấy từ read model teachers; Staff/Admin hiển thị theo vai trò (Academic không lưu hồ sơ nhân viên).
    private async Task<Dictionary<Guid, string>> NamesAsync(IEnumerable<PracticeExercise> items, CancellationToken ct)
    {
        var result = new Dictionary<Guid, string>();
        foreach (var x in items)
        {
            if (result.ContainsKey(x.CreatedBy)) continue;
            var teacher = x.CreatedByRole == Roles.Teacher ? await classes.FindTeacherAsync(x.CreatedBy, ct) : null;
            result[x.CreatedBy] = teacher?.FullName ?? x.CreatedByRole switch
            {
                Roles.Admin => "Quản trị viên",
                Roles.Staff => "Nhân viên",
                Roles.Teacher => "Giáo viên",
                _ => x.CreatedByRole
            };
        }
        return result;
    }

    private static string Preview(string content) =>
        content.Length <= PreviewLength ? content : content[..PreviewLength].TrimEnd() + "…";
}
