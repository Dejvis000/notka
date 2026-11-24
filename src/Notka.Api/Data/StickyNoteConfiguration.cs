using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Notka.Core;

namespace Notka.Api.Data
{
    internal sealed class StickyNoteConfiguration : IEntityTypeConfiguration<StickyNote>
    {
        public void Configure(EntityTypeBuilder<StickyNote> builder)
        {
            builder.Property(n => n.Color).HasMaxLength(StickyNote.ColorMaxLength);
        }
    }
}
