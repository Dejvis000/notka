using System.ComponentModel.DataAnnotations;

namespace Notka.Core
{
    public class TaskRequest
    {
        [Required(ErrorMessage = "Tytuł jest wymagany.")]
        [MaxLength(TaskItem.TitleMaxLength, ErrorMessage = "Tytuł może mieć maksymalnie {1} znaków.")]
        public string Title { get; set; } = null!;

        [MaxLength(TaskItem.NotesMaxLength, ErrorMessage = "Notatki mogą mieć maksymalnie {1} znaków.")]
        public string? Notes { get; set; }

        public DateTime? DueAt { get; set; }

        [EnumDataType(typeof(Priority), ErrorMessage = "Dozwolone priorytety: Low, Normal, High.")]
        public Priority Priority { get; set; } = Priority.Normal;

        [MaxLength(TaskItem.MaxTags, ErrorMessage = "Zadanie może mieć maksymalnie {1} tagów.")]
        public List<string>? Tags { get; set; }
    }
}
