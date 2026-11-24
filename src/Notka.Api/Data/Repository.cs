using Microsoft.EntityFrameworkCore;

namespace Notka.Api.Data
{
    internal interface IRepository<TEntity>
        where TEntity : class
    {
        IQueryable<TEntity> Query();

        ValueTask<TEntity?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

        void Add(TEntity entity);

        void Remove(TEntity entity);

        Task SaveChangesAsync(CancellationToken cancellationToken = default);
    }

    internal sealed class Repository<TEntity>(NotkaDbContext context) : IRepository<TEntity>
        where TEntity : class
    {
        private readonly NotkaDbContext _context = context;

        public IQueryable<TEntity> Query() => _context.Set<TEntity>().AsNoTracking();

        public ValueTask<TEntity?> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
            _context.Set<TEntity>().FindAsync([id], cancellationToken);

        public void Add(TEntity entity) => _context.Set<TEntity>().Add(entity);

        public void Remove(TEntity entity) => _context.Set<TEntity>().Remove(entity);

        public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
            _context.SaveChangesAsync(cancellationToken);
    }
}
