using EnglishCenter.Modules.Academic.Domain.Assignments;
using Xunit;

namespace EnglishCenter.Academic.Tests;

public sealed class ExamPromptDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 19, 8, 0, 0, TimeSpan.Zero);
    private static readonly Guid Owner = Guid.NewGuid();

    private static Assignment Exam(Guid? session = null) => Assignment.Create(Guid.NewGuid(), "Thi giữa kỳ", "", Now, Now.AddHours(2),
        AssignmentKind.Exam, null, null, session ?? Guid.NewGuid(), Guid.NewGuid(), Now);

    private static void Attach(Assignment item, Guid? file = null, string name = "de.pdf") =>
        item.AttachPrompt(file ?? Guid.NewGuid(), Owner, name, "application/pdf", 1024);

    [Fact]
    public void ExamPrompt_IsStoredAsSnapshot()
    {
        var item = Exam(); var file = Guid.NewGuid();
        Attach(item, file);
        Assert.Equal((file, Owner, "de.pdf", "application/pdf", 1024L), (item.PromptFileId, item.PromptOwnerId, item.PromptFileName,
            item.PromptContentType, item.PromptSizeBytes));
    }

    [Fact]
    public void Exam_CannotBePublishedWithoutPrompt()
    {
        var item = Exam();
        Assert.Throws<InvalidOperationException>(() => item.Publish(Guid.NewGuid(), Now));
        Attach(item);
        item.Publish(Guid.NewGuid(), Now);
        Assert.True(item.IsPublished);
    }

    [Theory]
    [InlineData(AssignmentKind.Homework)]
    [InlineData(AssignmentKind.InClass)]
    public void NonExamPrompt_IsOptionalAndLocksAfterPublishing(AssignmentKind kind)
    {
        var item = Assignment.Create(Guid.NewGuid(), "Bài", "", Now, Now.AddHours(1), kind, null, null, Guid.NewGuid(), Guid.NewGuid(), Now);
        Assert.Null(item.PromptFileId);
        var file = Guid.NewGuid();
        Attach(item, file);
        item.Publish(Owner, Now);
        Assert.Equal(file, item.PromptFileId);
        Assert.Throws<InvalidOperationException>(() => Attach(item));
    }

    [Fact]
    public void PromptCannotChange_OnceExamIsVisible()
    {
        var item = Exam(); Attach(item); item.Publish(Guid.NewGuid(), Now);
        Assert.Throws<InvalidOperationException>(() => Attach(item));
    }

    [Fact]
    public void DraftExam_CanReplacePrompt_AndLeavingExamDropsIt()
    {
        var item = Exam(); Attach(item, name: "cu.pdf"); Attach(item, name: "moi.pdf");
        Assert.Equal("moi.pdf", item.PromptFileName);
        item.Update("Bài thường", "", Now, Now.AddHours(2), AssignmentKind.Homework, null, null, null, Now);
        Assert.Null(item.PromptFileId); Assert.Null(item.PromptOwnerId); Assert.Null(item.PromptFileName);
        Assert.Null(item.PromptContentType); Assert.Null(item.PromptSizeBytes);
    }

    [Theory]
    [InlineData("", "application/pdf", 10L)]
    [InlineData("de.pdf", "", 10L)]
    [InlineData("de.pdf", "application/pdf", 0L)]
    [InlineData("de.pdf", "application/pdf", 20971521L)]
    public void InvalidPromptMetadata_IsRejected(string name, string type, long size)
    {
        var item = Exam();
        Assert.Throws<ArgumentException>(() => item.AttachPrompt(Guid.NewGuid(), Owner, name, type, size));
        Assert.Throws<ArgumentException>(() => item.AttachPrompt(Guid.Empty, Owner, "de.pdf", "application/pdf", 10));
        Assert.Throws<ArgumentException>(() => item.AttachPrompt(Guid.NewGuid(), Guid.Empty, "de.pdf", "application/pdf", 10));
    }
}
