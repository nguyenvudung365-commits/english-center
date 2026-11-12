namespace EnglishCenter.Modules.Academic.Domain.Assignments;

public sealed class Submission
{
    private Submission() { }
    public Guid Id { get; private set; }
    public Guid AssignmentId { get; private set; }
    public Guid StudentId { get; private set; }
    public Guid Revision { get; private set; }
    public DateTimeOffset SubmittedAt { get; private set; }
    public DateTimeOffset? GradedAt { get; private set; }
    private readonly List<SubmissionFile> files = [];
    public IReadOnlyCollection<SubmissionFile> Files => files.AsReadOnly();

    public static Submission Create(Guid assignmentId, Guid studentId)
    {
        if (assignmentId == Guid.Empty || studentId == Guid.Empty) throw new ArgumentException("Assignment and student required.");
        return new() { Id = Guid.NewGuid(), AssignmentId = assignmentId, StudentId = studentId };
    }
    public void ReplaceFiles(IReadOnlyList<SubmissionFile> replacements, DateTimeOffset now)
    {
        if (GradedAt is not null) throw new InvalidOperationException("Graded work cannot be replaced.");
        if (replacements.Count is < 1 or > 5 || replacements.Any(x => x.SubmissionId != Id) ||
            replacements.Select(x => x.FileId).Distinct().Count() != replacements.Count)
            throw new ArgumentException("One to five distinct attachments required.");
        var ids = replacements.Select(x => x.FileId).ToHashSet();
        files.RemoveAll(x => !ids.Contains(x.FileId));
        foreach (var replacement in replacements)
            if (files.All(x => x.FileId != replacement.FileId)) files.Add(replacement);
        SubmittedAt = now; Revision = Guid.NewGuid();
    }
}

public sealed class SubmissionFile
{
    private SubmissionFile() { }
    public Guid Id { get; private set; }
    public Guid SubmissionId { get; private set; }
    public Guid FileId { get; private set; }
    public string FileName { get; private set; } = string.Empty;
    public string ContentType { get; private set; } = string.Empty;
    public long SizeBytes { get; private set; }
    public static SubmissionFile Create(Guid submissionId, Guid fileId, string name, string contentType, long size)
    {
        if (submissionId == Guid.Empty || fileId == Guid.Empty || string.IsNullOrWhiteSpace(name) || name.Length > 255 ||
            string.IsNullOrWhiteSpace(contentType) || contentType.Length > 128 || size is <= 0 or > 20 * 1024 * 1024)
            throw new ArgumentException("Invalid submission attachment.");
        return new() { Id = Guid.NewGuid(), SubmissionId = submissionId, FileId = fileId, FileName = name, ContentType = contentType, SizeBytes = size };
    }
}
