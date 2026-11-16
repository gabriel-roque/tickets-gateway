using EventSphere.Application;
using EventSphere.Domain;
using EventSphere.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EventSphere.Infrastructure.Services;

public sealed class PriceCatalog(WriteDbContext database) : IPriceCatalog
{
    public async Task SetAsync(Guid inventoryId, Guid showId, Money price, CancellationToken cancellationToken)
    {
        var existing = await database.InventoryPrices.SingleOrDefaultAsync(x => x.InventoryId == inventoryId, cancellationToken);
        if (existing is null)
        {
            database.InventoryPrices.Add(new InventoryPriceRecord
            {
                InventoryId = inventoryId,
                ShowId = showId,
                Amount = price.Amount,
                Currency = price.Currency
            });
        }
        else if (existing.Amount != price.Amount || existing.Currency != price.Currency || existing.ShowId != showId)
        {
            throw new DomainRuleViolationException("Inventory catalog idempotency conflict.");
        }
        await database.SaveChangesAsync(cancellationToken);
    }

    public async Task<Money> GetAsync(Guid inventoryId, CancellationToken cancellationToken)
    {
        var price = await database.InventoryPrices.AsNoTracking()
            .SingleOrDefaultAsync(x => x.InventoryId == inventoryId, cancellationToken)
            ?? throw new DomainRuleViolationException("Inventory has no active catalog price.");
        return Money.Create(price.Amount, price.Currency);
    }
}
