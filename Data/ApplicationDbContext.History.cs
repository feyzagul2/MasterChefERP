using System.Security.Claims;
using MasterChefTakip.Models;
using MasterChefTakip.Services;
using Microsoft.EntityFrameworkCore;

namespace MasterChefTakip.Data;

public partial class ApplicationDbContext
{
    public DbSet<HistoryEntry> HistoryEntries => Set<HistoryEntry>();

    public override async Task<int> SaveChangesAsync(
        CancellationToken cancellationToken = default)
    {
        var actorId = _historyAccessor.HttpContext?.User
            .FindFirstValue(ClaimTypes.NameIdentifier);

        ChangeTracker.DetectChanges();

        var changes = ChangeTracker.Entries()
            .Where(e =>
                e.State is EntityState.Added
                    or EntityState.Modified
                    or EntityState.Deleted)
            .Where(e =>
                e.Entity is StockMovement
                    or Shipment
                    or ReturnInspection
                    or ReturnReceipt
                    or Pallet)
            .Select(e => new
            {
                e.Entity,
                Added = e.State == EntityState.Added,
                Deleted = e.State == EntityState.Deleted,

                Before = e.Properties
                    .Where(p => p.IsModified)
                    .ToDictionary(
                        p => p.Metadata.Name,
                        p => p.OriginalValue)
            })
            .ToList();

        if (changes.Count == 0 || string.IsNullOrWhiteSpace(actorId))
            return await base.SaveChangesAsync(cancellationToken);

        var ownsTransaction = Database.CurrentTransaction is null;

        var transaction = ownsTransaction
            ? await Database.BeginTransactionAsync(cancellationToken)
            : null;

        try
        {
            var result = await base.SaveChangesAsync(cancellationToken);
            var rows = new List<HistoryEntry>();

            foreach (var change in changes)
            {
                rows.AddRange(await HistoryWriter.BuildAsync(
                    this,
                    change.Entity,
                    change.Before,
                    actorId,
                    change.Added,
                    change.Deleted,
                    cancellationToken));
            }

            await HistoryWriter.AddMissingAsync(
                this,
                rows,
                cancellationToken);

            await base.SaveChangesAsync(cancellationToken);

            if (transaction is not null)
                await transaction.CommitAsync(cancellationToken);

            return result;
        }
        catch
        {
            if (transaction is not null)
                await transaction.RollbackAsync(cancellationToken);

            throw;
        }
        finally
        {
            if (transaction is not null)
                await transaction.DisposeAsync();
        }
    }
}