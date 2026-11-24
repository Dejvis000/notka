using Microsoft.EntityFrameworkCore;
using Notka.Core;

namespace Notka.Api.Data
{
    public class NotkaDbContext(DbContextOptions<NotkaDbContext> options, TimeProvider time) : DbContext(options)
    {
        private readonly TimeProvider _time = time;

        public DbSet<TaskItem> Tasks => Set<TaskItem>();

        public DbSet<StickyNote> Notes => Set<StickyNote>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfigurationsFromAssembly(typeof(NotkaDbContext).Assembly);
        }

        public override int SaveChanges(bool acceptAllChangesOnSuccess)
        {
            Stamp();
            return base.SaveChanges(acceptAllChangesOnSuccess);
        }

        public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
        {
            Stamp();
            return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }

        private void Stamp()
        {
            var now = _time.GetLocalNow().DateTime;
            foreach (var entry in ChangeTracker.Entries<ITrackedEntity>())
            {
                if (entry.State == EntityState.Added)
                {
                    entry.Entity.CreatedAt = now;
                }

                if (entry.State is EntityState.Added or EntityState.Modified)
                {
                    entry.Entity.UpdatedAt = now;
                }
            }
        }
    }
}
