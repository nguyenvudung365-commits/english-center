namespace EnglishCenter.BuildingBlocks.Security;

/// <summary>Danh tính người gọi hiện tại. Không phụ thuộc ASP.NET Core; bản triển khai nằm ở Host.</summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }
    /// <summary>Null nếu chưa xác thực hoặc claim <c>sub</c> không phải Guid.</summary>
    Guid? UserId { get; }
    /// <summary>Một trong <see cref="Roles"/>; null nếu không có.</summary>
    string? Role { get; }
    bool IsInRole(string role);
}
