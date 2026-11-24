namespace Notka.Core
{
    public class StickyNote : ITrackedEntity
    {
        public const int ContentMaxLength = 10000;
        public const int ColorMaxLength = 32;

        public int Id { get; set; }

        public string Content { get; set; } = "";

        public string Color { get; set; } = "yellow";

        public bool Pinned { get; set; }

        public DateTime CreatedAt { get; set; }

        public DateTime UpdatedAt { get; set; }
    }
}
