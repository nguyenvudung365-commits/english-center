namespace EnglishCenter.Modules.Academic.Application;

public sealed class AcademicFailure(int status, string message, IReadOnlyDictionary<string, string[]>? errors = null)
    : Exception(message)
{
    public int Status { get; } = status;

    // Lỗi theo từng trường (chỉ dùng cho 400), Api trả về dạng ValidationProblemDetails.
    public IReadOnlyDictionary<string, string[]>? Errors { get; } = errors;
}
