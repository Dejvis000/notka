using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Notka.Core;

namespace Notka.Api.Data
{
    internal sealed class TaskItemConfiguration : IEntityTypeConfiguration<TaskItem>
    {
        public void Configure(EntityTypeBuilder<TaskItem> builder)
        {
            builder.Property(t => t.Title).HasMaxLength(TaskItem.TitleMaxLength);
            builder.HasIndex(t => new { t.IsDone, t.DueAt });
        }
    }
}
