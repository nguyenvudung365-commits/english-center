using EnglishCenter.Modules.Academic.Application.Assignments;
using EnglishCenter.Modules.Academic.Domain.Assignments;
using EnglishCenter.Modules.Academic.Domain.Practice;
using Xunit;

namespace EnglishCenter.Academic.Tests;

public sealed class Tv2CourseworkDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 15, 3, 0, 0, TimeSpan.Zero);

    private static Submission SubmittedWork()
    {
        var item = Submission.Create(Guid.NewGuid(), Guid.NewGuid());
        item.ReplaceFiles([SubmissionFile.Create(item.Id, Guid.NewGuid(), "bai.pdf", "application/pdf", 100)], Now);
        return item;
    }

    [Theory]
    [InlineData("0", true)]
    [InlineData("10", true)]
    [InlineData("7.5", true)]
    [InlineData("0.1", true)]
    [InlineData("9.9", true)]
    [InlineData("-0.1", false)]
    [InlineData("10.1", false)]
    [InlineData("7.55", false)]
    [InlineData("100", false)]
    public void Score_IsZeroToTenInStepsOfOneTenth(string value, bool valid) =>
        Assert.Equal(valid, Submission.IsValidScore(decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture)));

    [Fact]
    public void UngradedSubmission_HasNullScoreNotZero()
    {
        var item = SubmittedWork();
        Assert.Null(item.Score);
        Assert.Equal(0, item.GradeVersion);
        Assert.Null(item.GradedAt);
    }

    [Fact]
    public void Grade_ThenRegrade_KeepsHistoryWithPreviousScoreAndIncrementsVersion()
    {
        var item = SubmittedWork(); var teacher = Guid.NewGuid();
        var revision = item.Revision;
        var first = item.Grade(8.5m, "  Tốt  ", teacher, 0, Now);
        Assert.Equal((1, 8.5m, (decimal?)null, "Tốt"), (first.Version, first.Score, first.PreviousScore, first.Feedback));
        Assert.NotEqual(revision, item.Revision);
        var second = item.Grade(9m, null, teacher, 1, Now.AddMinutes(5));
        Assert.Equal((2, 9m, (decimal?)8.5m, (string?)null), (second.Version, second.Score, second.PreviousScore, second.Feedback));
        Assert.Equal((9m, 2, teacher), (item.Score!.Value, item.GradeVersion, item.GradedBy!.Value));
        Assert.Null(first.EventSentAt);
    }

    [Fact]
    public void Grade_WithStaleVersion_IsConflictAndDoesNotChangeScore()
    {
        var item = SubmittedWork(); var teacher = Guid.NewGuid();
        item.Grade(6m, null, teacher, 0, Now);
        var conflict = Assert.Throws<GradeConflictException>(() => item.Grade(7m, null, teacher, 0, Now));
        Assert.Equal(1, conflict.CurrentVersion);
        Assert.Equal(6m, item.Score);
    }

    [Fact]
    public void Grade_RejectsInvalidScoreFeedbackAndGrader()
    {
        var item = SubmittedWork();
        Assert.Throws<ArgumentOutOfRangeException>(() => item.Grade(10.5m, null, Guid.NewGuid(), 0, Now));
        Assert.Throws<ArgumentException>(() => item.Grade(5m, new string('x', Submission.FeedbackMaxLength + 1), Guid.NewGuid(), 0, Now));
        Assert.Throws<ArgumentException>(() => item.Grade(5m, null, Guid.Empty, 0, Now));
        Assert.Equal(0, item.GradeVersion);
    }

    [Fact]
    public void SameGradeRetry_IsRecognisedOnlyForTheVersionJustWritten()
    {
        var item = SubmittedWork(); var teacher = Guid.NewGuid();
        item.Grade(8m, "Ổn", teacher, 0, Now);
        Assert.True(item.IsSameGrade(8m, " Ổn ", teacher, 0));
        Assert.False(item.IsSameGrade(8m, "Khác", teacher, 0));
        Assert.False(item.IsSameGrade(8m, "Ổn", Guid.NewGuid(), 0));
        Assert.False(item.IsSameGrade(8m, "Ổn", teacher, 1));
    }

    [Fact]
    public void GradedSubmission_CannotReplaceFiles()
    {
        var item = SubmittedWork();
        item.Grade(5m, null, Guid.NewGuid(), 0, Now);
        Assert.Throws<InvalidOperationException>(() =>
            item.ReplaceFiles([SubmissionFile.Create(item.Id, Guid.NewGuid(), "moi.pdf", "application/pdf", 10)], Now));
    }

    [Fact]
    public void Assignment_IsDraftUntilPublished_ThenStatusFollowsWindow()
    {
        var item = Assignment.Create(Guid.NewGuid(), "Unit 1", "", Now.AddHours(1), Now.AddHours(3), AssignmentKind.InClass,
            " Unit 1 ", "", null, Guid.NewGuid(), Now);
        Assert.Equal((AssignmentStatus.Draft, false, 1, "Unit 1", (string?)null), (item.StatusAt(Now), item.IsLegacy, item.Version, item.Unit, item.Lesson));
        item.Publish(Guid.NewGuid(), Now);
        Assert.Equal(2, item.Version);
        Assert.Equal(AssignmentStatus.Upcoming, item.StatusAt(Now));
        Assert.Equal(AssignmentStatus.Open, item.StatusAt(Now.AddHours(1)));
        Assert.Equal(AssignmentStatus.Closed, item.StatusAt(Now.AddHours(3)));
        Assert.Throws<InvalidOperationException>(() => item.Publish(Guid.NewGuid(), Now));
    }

    [Fact]
    public void PublishedAssignment_AllowsTextEditsButNotWindowKindOrSessionChanges()
    {
        var item = Assignment.Create(Guid.NewGuid(), "Bài 1", "", Now, Now.AddDays(1), AssignmentKind.Homework, null, null, null, Guid.NewGuid(), Now);
        item.Publish(Guid.NewGuid(), Now);
        item.Update("Bài 1 (sửa)", "Mô tả", Now, Now.AddDays(1), AssignmentKind.Homework, "Unit 2", "Lesson 3", null, Now);
        Assert.Equal(("Bài 1 (sửa)", "Unit 2", 3), (item.Title, item.Unit, item.Version));
        Assert.Throws<InvalidOperationException>(() => item.Update("x", "", Now, Now.AddDays(2), AssignmentKind.Homework, null, null, null, Now));
        Assert.Throws<InvalidOperationException>(() => item.Update("x", "", Now, Now.AddDays(1), AssignmentKind.InClass, null, null, null, Now));
        Assert.Throws<InvalidOperationException>(() => item.Update("x", "", Now, Now.AddDays(1), AssignmentKind.Homework, null, null, Guid.NewGuid(), Now));
    }

    [Fact]
    public void DraftAssignment_CanChangeEverything_ButValidatesInput()
    {
        var item = Assignment.Create(Guid.NewGuid(), "Nháp", "", Now, Now.AddDays(1), AssignmentKind.Homework, null, null, null, Guid.NewGuid(), Now);
        item.Update("Nháp", "", Now.AddDays(1), Now.AddDays(2), AssignmentKind.InClass, null, null, Guid.NewGuid(), Now);
        Assert.Equal(AssignmentKind.InClass, item.Kind);
        Assert.Throws<ArgumentException>(() => item.Update(" ", "", Now, Now.AddDays(1), AssignmentKind.Homework, null, null, null, Now));
        Assert.Throws<ArgumentException>(() => item.Update("x", "", Now, Now, AssignmentKind.Homework, null, null, null, Now));
        Assert.Throws<ArgumentException>(() => item.Update("x", "", Now, Now.AddDays(1), (AssignmentKind)9, null, null, null, Now));
        Assert.Throws<ArgumentException>(() => item.Update("x", "", Now, Now.AddDays(1), AssignmentKind.Homework, new string('u', 101), null, null, Now));
    }

    [Fact]
    public void Recipient_FinalOutcomeIsNeverReopened_AndOnlyMissedPublishes()
    {
        var item = AssignmentRecipient.Create(Guid.NewGuid(), Guid.NewGuid(), Now);
        Assert.Throws<InvalidOperationException>(() => item.MarkMissedEventSent(Now));
        item.Resolve(ObligationOutcome.Missed, Now);
        item.Resolve(ObligationOutcome.Submitted, Now.AddDays(1));
        Assert.Equal((ObligationOutcome.Missed, Now), (item.Outcome, item.ResolvedAt!.Value));
        item.MarkMissedEventSent(Now);
        item.MarkMissedEventSent(Now.AddDays(1));
        Assert.Equal(Now, item.MissedEventSentAt);
        Assert.Throws<ArgumentException>(() => item.Resolve(ObligationOutcome.Pending, Now));
    }

    [Fact]
    public void Practice_TrimsValidatesVersionsAndSoftDeletes()
    {
        var item = PracticeExercise.Create(Guid.NewGuid(), "  Đọc hiểu ", " Nội dung ", Guid.NewGuid(), "Teacher", Now);
        Assert.Equal(("Đọc hiểu", "Nội dung", 1), (item.Title, item.Content, item.Version));
        item.Update("Đọc hiểu 2", "Nội dung 2", Guid.NewGuid(), Now);
        Assert.Equal(2, item.Version);
        Assert.Throws<ArgumentException>(() => item.Update("", "x", Guid.NewGuid(), Now));
        Assert.Throws<ArgumentException>(() => item.Update("x", new string('c', PracticeExercise.ContentMaxLength + 1), Guid.NewGuid(), Now));
        item.Delete(Guid.NewGuid(), Now);
        Assert.True(item.IsDeleted);
        Assert.Equal(3, item.Version);
        item.Delete(Guid.NewGuid(), Now);
        Assert.Equal(3, item.Version);
        Assert.Throws<InvalidOperationException>(() => item.Update("x", "y", Guid.NewGuid(), Now));
    }

    [Fact]
    public void EventKeys_AreDeterministicPerBusinessKey()
    {
        var assignment = Guid.NewGuid(); var student = Guid.NewGuid();
        Assert.Equal(CourseworkEvents.Key("assignment-missed", assignment, student), CourseworkEvents.Key("assignment-missed", assignment, student));
        Assert.NotEqual(CourseworkEvents.Key("assignment-missed", assignment, student), CourseworkEvents.Key("assignment-missed", student, assignment));
        Assert.NotEqual(CourseworkEvents.Key("submission-graded", assignment, 1), CourseworkEvents.Key("submission-graded", assignment, 2));
        Assert.NotEqual(Guid.Empty, CourseworkEvents.Key("assignment-published", assignment));
    }
}
