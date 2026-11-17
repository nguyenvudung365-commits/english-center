namespace EnglishCenter.Modules.Enrollment.Domain.PlacementTests;

public sealed class TestTeacher
{
    public Guid TeacherId { get; private set; }
    private TestTeacher() { }
    public TestTeacher(Guid id) { TeacherId = id; }
}

public sealed class PlacementTest
{
    public Guid Id { get; private set; }
    public Guid StudentId { get; private set; }
    public string Location { get; private set; } = "";
    public string Mode { get; private set; } = "";
    public DateTime ScheduledAt { get; private set; }
    public string TestName { get; private set; } = "";
    public string Status { get; private set; } = "Pending";
    public Guid? TeacherId { get; private set; }
    public Guid CreatedBy { get; private set; }
    public DateTime CreatedAt { get; private set; }
    private PlacementTest() { }
    public PlacementTest(Guid student, Guid creator, DateTime createdAt)
    { Id = Guid.NewGuid(); StudentId = student; CreatedBy = creator; CreatedAt = createdAt; }
    public void Update(string location, string mode, DateTime at, string name, string status, Guid? teacher)
    { Location = location; Mode = mode; ScheduledAt = at; TestName = name; Status = status; TeacherId = teacher; }
}
