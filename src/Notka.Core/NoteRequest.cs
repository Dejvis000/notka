using System.ComponentModel.DataAnnotations;

namespace Notka.Core
{
    public class NoteRequest
    {
        [Required(AllowEmptyStrings = true, ErrorMessage = "Treść jest wymagana.")]
        [MaxLength(StickyNote.ContentMaxLength, ErrorMessage = "Treść może mieć maksymalnie {1} znaków.")]
        public string Content { get; set; } = null!;

        [Required(ErrorMessage = "Kolor jest wymagany.")]
        [MaxLength(StickyNote.ColorMaxLength, ErrorMessage = "Kolor może mieć maksymalnie {1} znaków.")]
        public string Color { get; set; } = "yellow";
    }
}
