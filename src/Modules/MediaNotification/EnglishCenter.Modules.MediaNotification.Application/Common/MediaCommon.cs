using EnglishCenter.BuildingBlocks.Security;
using EnglishCenter.Modules.Identity.Contracts;

namespace EnglishCenter.Modules.MediaNotification.Application.Common;

public sealed class MediaFailure(int status, string message) : Exception(message)
{
    public int Status { get; } = status;
}

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);

public sealed record Actor(Guid Id, string Role)
{
    public bool IsManager => Role is Roles.Staff or Roles.Admin;
}

public static class Paging
{
    public static (int Page, int Size) Validate(int page, int size)
    {
        if (page < 1 || page > 1_000_000 || size < 1) throw new MediaFailure(400, "Invalid pagination.");
        return (page, Math.Min(size, 100));
    }
}

public static class Text
{
    // Văn bản thuần: cắt khoảng trắng hai đầu, giới hạn độ dài, cấm ký tự điều khiển (trừ xuống dòng khi cho phép).
    public static string Clean(string? value, int max, string field, bool multiline = false)
    {
        var text = value?.Trim() ?? string.Empty;
        if (text.Length is 0 || text.Length > max || text.Any(c => char.IsControl(c) && !(multiline && c is '\n' or '\r' or '\t')))
            throw new MediaFailure(400, $"{field} must be 1-{max} plain-text characters.");
        return text;
    }

    public static string? Search(string? value) => string.IsNullOrWhiteSpace(value) ? null
        : value.Trim().Length <= 100 ? value.Trim() : throw new MediaFailure(400, "Search text is too long.");
}

// Mỗi request kiểm tra tài khoản còn hoạt động đúng vai trò (không tin token cũ).
public sealed class MediaAccess(ICurrentUser current, IAccountAccess accounts)
{
    public static readonly string[] Everyone = [Roles.Student, Roles.Teacher, Roles.Staff, Roles.Admin];

    public async Task<Actor> ActiveAsync(CancellationToken ct, params string[] allowed)
    {
        if (!current.IsAuthenticated || current.UserId is not { } id || id == Guid.Empty || current.Role is not { } role)
            throw new MediaFailure(401, "Authentication required.");
        if (!allowed.Contains(role) || !await accounts.IsActiveInRoleAsync(id, role, ct))
            throw new MediaFailure(403, "An active account with an allowed role is required.");
        return new(id, role);
    }
}
