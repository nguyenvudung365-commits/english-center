namespace EnglishCenter.Modules.MediaNotification.Domain.Faqs;

public sealed class Faq
{
    private Faq() { }

    public Guid Id { get; private set; }
    public string Question { get; private set; } = string.Empty;
    public string Answer { get; private set; } = string.Empty;
    public bool IsPublished { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }
    public Guid UpdatedBy { get; private set; }

    public static Faq Create(string question, string answer, bool published, Guid actor, DateTime now) => new()
    {
        Id = Guid.NewGuid(), Question = question, Answer = answer, IsPublished = published,
        CreatedAt = now, UpdatedAt = now, UpdatedBy = actor
    };

    public void Update(string question, string answer, bool published, Guid actor, DateTime now)
    {
        Question = question;
        Answer = answer;
        IsPublished = published;
        UpdatedAt = now;
        UpdatedBy = actor;
    }
}
