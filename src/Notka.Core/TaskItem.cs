namespace Notka.Core
{
    public class TaskItem
    {
        public const int TitleMaxLength = 500;
        public const int NotesMaxLength = 10000;
        public const int MaxTags = 20;
        public const int TagMaxLength = 50;

        public int Id { get; set; }

        public string Title { get; set; } = "";

        public string? Notes { get; set; }

        public DateTime? DueAt { get; set; }

        public Priority Priority { get; set; } = Priority.Normal;

        public bool IsDone { get; set; }

        public DateTime CreatedAt { get; set; }

        public DateTime? CompletedAt { get; set; }

        public List<string> Tags { get; set; } = [];
    }
}
