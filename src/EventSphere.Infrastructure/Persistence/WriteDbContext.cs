using Microsoft.EntityFrameworkCore;

namespace EventSphere.Infrastructure.Persistence;

public sealed class WriteDbContext(DbContextOptions<WriteDbContext> options) : DbContext(options)
{
    public DbSet<EventRecord> Events => Set<EventRecord>();
    public DbSet<OutboxRecord> Outbox => Set<OutboxRecord>();
    public DbSet<InboxRecord> Inbox => Set<InboxRecord>();
    public DbSet<SagaRecord> Sagas => Set<SagaRecord>();
    public DbSet<IdempotencyRecord> Idempotency => Set<IdempotencyRecord>();
    public DbSet<PaymentRecord> Payments => Set<PaymentRecord>();
    public DbSet<PricingLockRecord> PricingLocks => Set<PricingLockRecord>();
    public DbSet<InventoryPriceRecord> InventoryPrices => Set<InventoryPriceRecord>();
    public DbSet<InventoryReservationRecord> InventoryReservations => Set<InventoryReservationRecord>();
    public DbSet<FaultInjectionRecord> FaultInjections => Set<FaultInjectionRecord>();
    public DbSet<TicketIndexRecord> TicketIndex => Set<TicketIndexRecord>();
    public DbSet<SettlementRecord> Settlements => Set<SettlementRecord>();
    public DbSet<NotificationRecord> Notifications => Set<NotificationRecord>();
    public DbSet<LifecycleWorkflowRecord> LifecycleWorkflows => Set<LifecycleWorkflowRecord>();
    public DbSet<SeatLockRecord> SeatLocks => Set<SeatLockRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<EventRecord>(builder =>
        {
            builder.HasKey(x => x.GlobalPosition);
            builder.Property(x => x.GlobalPosition).ValueGeneratedOnAdd();
            builder.HasIndex(x => x.EventId).IsUnique();
            builder.HasIndex(x => new { x.StreamId, x.StreamVersion }).IsUnique();
            builder.Property(x => x.StreamId).HasMaxLength(160);
            builder.Property(x => x.EventType).HasMaxLength(250);
        });
        modelBuilder.Entity<OutboxRecord>().HasKey(x => x.MessageId);
        modelBuilder.Entity<OutboxRecord>().HasIndex(x => new { x.PublishedAt, x.DeadLetteredAt, x.NextAttemptAt, x.LeaseUntil });
        modelBuilder.Entity<InboxRecord>().HasKey(x => x.MessageId);
        modelBuilder.Entity<SagaRecord>(builder =>
        {
            builder.HasKey(x => x.Id);
            builder.Property(x => x.Version).IsConcurrencyToken();
            builder.HasIndex(x => new { x.Status, x.NextAttemptAt, x.LeaseUntil });
            builder.HasIndex(x => x.OrderId).IsUnique();
            builder.Property(x => x.UnitPrice).HasPrecision(18, 2);
        });
        modelBuilder.Entity<IdempotencyRecord>(builder =>
        {
            builder.HasKey(x => x.Key);
            builder.Property(x => x.Key).HasMaxLength(128);
            builder.Property(x => x.RawKey).HasMaxLength(128);
            builder.HasIndex(x => x.RawKey);
            builder.Property(x => x.Fingerprint).HasMaxLength(64);
        });
        modelBuilder.Entity<PaymentRecord>(builder =>
        {
            builder.HasKey(x => x.Id);
            builder.HasIndex(x => x.OrderId).IsUnique();
            builder.HasIndex(x => x.ExternalId).IsUnique();
            builder.Property(x => x.Amount).HasPrecision(18, 2);
        });
        modelBuilder.Entity<PricingLockRecord>(builder =>
        {
            builder.HasKey(x => x.SagaId);
            builder.HasIndex(x => x.OrderId).IsUnique();
            builder.Property(x => x.Amount).HasPrecision(18, 2);
        });
        modelBuilder.Entity<InventoryPriceRecord>(builder =>
        {
            builder.HasKey(x => x.InventoryId);
            builder.Property(x => x.Version).IsConcurrencyToken();
            builder.HasIndex(x => x.ShowId);
            builder.Property(x => x.Amount).HasPrecision(18, 2);
        });
        modelBuilder.Entity<InventoryReservationRecord>(builder =>
        {
            builder.HasKey(x => x.ReservationId);
            builder.HasIndex(x => new { x.Status, x.ExpiresAt });
            builder.HasIndex(x => x.OrderId).IsUnique();
        });
        modelBuilder.Entity<FaultInjectionRecord>().HasKey(x => x.Key);
        modelBuilder.Entity<TicketIndexRecord>().HasKey(x => x.TicketId);
        modelBuilder.Entity<TicketIndexRecord>().HasIndex(x => x.OrderId).IsUnique();
        modelBuilder.Entity<SettlementRecord>(builder =>
        {
            builder.HasKey(x => x.OrderId);
            builder.Property(x => x.GrossAmount).HasPrecision(18, 2);
            builder.Property(x => x.PlatformFee).HasPrecision(18, 2);
            builder.Property(x => x.OrganizerAmount).HasPrecision(18, 2);
        });
        modelBuilder.Entity<NotificationRecord>().HasKey(x => x.Id);
        modelBuilder.Entity<NotificationRecord>().HasIndex(x => new { x.OrderId, x.Template }).IsUnique();
        modelBuilder.Entity<LifecycleWorkflowRecord>(builder =>
        {
            builder.HasKey(x => x.Id);
            builder.Property(x => x.Version).IsConcurrencyToken();
            builder.HasIndex(x => new { x.Type, x.AggregateId });
            builder.HasIndex(x => x.InitiatorId);
            builder.HasIndex(x => new { x.Status, x.NextAttemptAt, x.LeaseUntil });
        });
        modelBuilder.Entity<SeatLockRecord>(builder =>
        {
            builder.HasKey(x => new { x.ShowId, x.Sector, x.Row, x.Number });
            builder.HasIndex(x => x.ReservationId).IsUnique();
            builder.HasIndex(x => x.OwnerId);
            builder.Property(x => x.Sector).HasMaxLength(64);
            builder.Property(x => x.Row).HasMaxLength(32);
            builder.Property(x => x.Number).HasMaxLength(32);
        });
    }
}
