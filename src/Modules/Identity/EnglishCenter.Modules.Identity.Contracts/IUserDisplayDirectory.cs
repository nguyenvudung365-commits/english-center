namespace EnglishCenter.Modules.Identity.Contracts;

// Chỉ trả tên hiển thị (không email/điện thoại); id không tồn tại thì vắng mặt trong kết quả.
public interface IUserDisplayDirectory
{
    Task<IReadOnlyDictionary<Guid, string>> FindNamesAsync(IReadOnlyCollection<Guid> userIds, CancellationToken ct);
}
