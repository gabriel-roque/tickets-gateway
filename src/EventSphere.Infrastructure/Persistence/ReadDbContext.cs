using EventSphere.Application;
using Microsoft.EntityFrameworkCore;

namespace EventSphere.Infrastructure.Persistence;

public sealed class ReadDbContext(DbContextOptions<ReadDbContext> options) : DbContext(options)
{
    public DbSet<OrderReadModel> Orders => Set<OrderReadModel>();
    public DbSet<InventoryReadModel> Inventories => Set<InventoryReadModel>();
    public DbSet<TicketReadModel> Tickets => Set<TicketReadModel>();
    public DbSet<ProjectionCheckpointRecord> Checkpoints => Set<ProjectionCheckpointRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OrderReadModel>(builder =>
        {
            builder.HasKey(x => x.OrderId);
            builder.Property(x => x.Total).HasPrecision(18, 2);
        });
        modelBuilder.Entity<InventoryReadModel>().HasKey(x => x.InventoryId);
        modelBuilder.Entity<TicketReadModel>().HasKey(x => x.TicketId);
        modelBuilder.Entity<ProjectionCheckpointRecord>().HasKey(x => x.Projector);
    }
}
