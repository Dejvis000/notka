using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Notka.Api.Data
{
    internal sealed class DesignTimeFactory : IDesignTimeDbContextFactory<NotkaDbContext>
    {
        public NotkaDbContext CreateDbContext(string[] args) =>
            new(new DbContextOptionsBuilder<NotkaDbContext>().UseSqlite("Data Source=design.db").Options, TimeProvider.System);
    }
}
