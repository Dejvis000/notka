namespace Notka.Core
{
    public interface ITrackedEntity
    {
        DateTime CreatedAt { get; set; }

        DateTime UpdatedAt { get; set; }
    }
}
