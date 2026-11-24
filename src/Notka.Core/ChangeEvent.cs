namespace Notka.Core
{
    public class ChangeEvent
    {
        public required string Kind { get; set; }

        public required int Id { get; set; }
    }
}
