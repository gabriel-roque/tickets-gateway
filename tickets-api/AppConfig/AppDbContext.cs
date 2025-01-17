
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using TicketsApi.Models;

namespace TicketsApi.AppConfig;

public class AppDbContext(DbContextOptions options) : IdentityDbContext<User, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<Event> Event { get; set; }
    public DbSet<Ticket> Ticket { get; set; }
    
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        foreach (var entityType in builder.Model.GetEntityTypes())
        {
            var tableName = entityType.GetTableName();
            if (tableName.StartsWith("AspNet"))
                entityType.SetTableName(tableName.Substring(6));
            
            Map.Setup(builder);
        }
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = new())
    {
        OnBeforeSaving();
        return base.SaveChangesAsync(cancellationToken);
    }

    private void OnBeforeSaving()
    {
        foreach (var entry in ChangeTracker.Entries<Event>())
            switch (entry.State)
            {
                case EntityState.Modified:
                    entry.Entity.Version =  entry.Entity.Version == 0 ? 1 : entry.Entity.Version + 1;
                    break;
            }
        
        foreach (var entry in ChangeTracker.Entries<Entity>())
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.CurrentValues["Deleted"] = false;
                    break;
                case EntityState.Modified:
                    entry.Entity.UpdateDate = DateTimeOffset.UtcNow;
                    break;
                case EntityState.Deleted:
                    entry.State = EntityState.Modified;
                    entry.CurrentValues["Deleted"] = true;
                    break;
            }
    }
}
