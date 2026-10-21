using MasterChefTakip.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace MasterChefTakip.Data;

public partial class ApplicationDbContext(
    DbContextOptions<ApplicationDbContext> options,
    Microsoft.AspNetCore.Http.IHttpContextAccessor historyAccessor)
    : IdentityDbContext<ApplicationUser>(options)
{
    private readonly Microsoft.AspNetCore.Http.IHttpContextAccessor
        _historyAccessor = historyAccessor;

    public DbSet<Product> Products => Set<Product>();

    public DbSet<ProductSalesOption> ProductSalesOptions => Set<ProductSalesOption>();

    public DbSet<SalesBundle> SalesBundles => Set<SalesBundle>();

    public DbSet<SalesBundleComponent> SalesBundleComponents => Set<SalesBundleComponent>();

    public DbSet<Pallet> Pallets => Set<Pallet>();

    public DbSet<PalletItem> PalletItems => Set<PalletItem>();

    public DbSet<StockMovement> StockMovements => Set<StockMovement>();

    public DbSet<Shipment> Shipments => Set<Shipment>();

    public DbSet<ShipmentItem> ShipmentItems => Set<ShipmentItem>();
    public DbSet<ShipmentPackage> ShipmentPackages
    => Set<ShipmentPackage>();

    public DbSet<ShipmentPackageItem> ShipmentPackageItems
    => Set<ShipmentPackageItem>();

    public DbSet<PackagingBatch> PackagingBatches => Set<PackagingBatch>();

    public DbSet<ReturnInspection> ReturnInspections
        => Set<ReturnInspection>();

    public DbSet<ReturnReceipt> ReturnReceipts
        => Set<ReturnReceipt>();

    public DbSet<ReturnPlacement> ReturnPlacements
        => Set<ReturnPlacement>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<ApplicationUser>()
            .Property(user => user.IsActive)
            .HasDefaultValue(true);

        builder.Entity<Product>(entity =>
        {
            entity.HasKey(product => product.Id);

            entity.Property(product => product.Code)
                .IsRequired()
                .HasMaxLength(50)
                .UseCollation("NOCASE");

            entity.HasIndex(product => product.Code)
                .IsUnique();

            entity.HasIndex(product => product.Barcode)
                .IsUnique();

            entity.Property(product => product.Name)
                .IsRequired()
                .HasMaxLength(200);

            entity.Property(product => product.PackagingType)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(product => product.IsActive)
                .HasDefaultValue(true);
        });

        builder.Entity<Pallet>()
            .HasIndex(pallet => pallet.Barcode)
            .IsUnique();

        builder.Entity<PalletItem>(entity =>
        {
            // Aynı ürün aynı palette farklı stok kayıtlarında olabilir.
            entity.HasIndex(item => new
            {
                item.PalletId,
                item.ProductId
            });

            entity.HasOne(item => item.Pallet)
                .WithMany()
                .HasForeignKey(item => item.PalletId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(item => item.Product)
                .WithMany()
                .HasForeignKey(item => item.ProductId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<PackagingBatch>()
            .HasKey(batch => batch.Id);

        builder.Entity<StockMovement>(entity =>
        {
            entity.HasOne(movement => movement.PalletItem)
                .WithMany()
                .HasForeignKey(movement => movement.PalletItemId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(movement => new
            {
                movement.PalletItemId,
                movement.CreatedAtUtc
            });

            entity.HasOne<PackagingBatch>()
                .WithMany()
                .HasForeignKey(movement => movement.PackagingBatchId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<Shipment>(entity =>
        {
            entity.HasIndex(shipment => shipment.Number)
                .IsUnique();

            entity.Property(shipment => shipment.Number)
                .IsRequired()
                .HasMaxLength(50);

            entity.Property(shipment => shipment.RecipientName)
                .IsRequired()
                .HasMaxLength(200);

            entity.HasIndex(shipment => new
            {
                shipment.Status,
                shipment.CreatedAtUtc
            });
        });

        builder.Entity<ShipmentItem>(entity =>
        {
            entity.HasIndex(item => new
            {
                item.ShipmentId,
                item.PalletItemId
            })
                .IsUnique();

            entity.HasOne(item => item.Shipment)
                .WithMany(shipment => shipment.Items)
                .HasForeignKey(item => item.ShipmentId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(item => item.PalletItem)
                .WithMany()
                .HasForeignKey(item => item.PalletItemId)
                .OnDelete(DeleteBehavior.Restrict);
        });
                builder.Entity<ShipmentPackage>(entity =>
        {
            entity.HasOne(package => package.Shipment)
                .WithMany(shipment => shipment.Packages)
                .HasForeignKey(package => package.ShipmentId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<ShipmentPackageItem>(entity =>
        {
            entity.HasOne(item => item.ShipmentPackage)
                .WithMany(package => package.Items)
                .HasForeignKey(item => item.ShipmentPackageId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(item => item.ShipmentItem)
                .WithMany()
                .HasForeignKey(item => item.ShipmentItemId)
                .OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<ReturnReceipt>(entity =>
        {
            entity.HasOne(receipt => receipt.Shipment)
                .WithMany()
                .HasForeignKey(receipt => receipt.ShipmentId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(receipt => new
            {
                receipt.Status,
                receipt.CreatedAtUtc
            });
        });

        builder.Entity<ReturnInspection>(entity =>
        {
            entity.HasOne(item => item.ReturnReceipt)
                .WithMany(receipt => receipt.Items)
                .HasForeignKey(item => item.ReturnReceiptId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(item => item.ShipmentItem)
                .WithMany()
                .HasForeignKey(item => item.ShipmentItemId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(item => item.DestinationPallet)
                .WithMany()
                .HasForeignKey(item => item.DestinationPalletId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(item => new
            {
                item.Status,
                item.CreatedAtUtc
            });

            entity.HasIndex(item => new
            {
                item.ReturnReceiptId,
                item.ShipmentItemId
            });
        });

        builder.Entity<ReturnPlacement>(entity =>
        {
            entity.HasOne(placement => placement.ReturnInspection)
                .WithMany(item => item.Placements)
                .HasForeignKey(placement => placement.ReturnInspectionId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(placement => placement.Pallet)
                .WithMany()
                .HasForeignKey(placement => placement.PalletId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(placement => placement.PalletItem)
                .WithMany()
                .HasForeignKey(placement => placement.PalletItemId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(placement => placement.PalletItemId)
                .IsUnique();
        });

        // EF_CONCURRENCY_PROTECTION_2026
        // Prevent simultaneous updates from silently overwriting stock.
        foreach (var entityType in builder.Model.GetEntityTypes())
        {
            var stamp = entityType.FindProperty("ConcurrencyStamp");

            if (stamp is not null &&
                stamp.ClrType == typeof(string))
            {
                stamp.IsConcurrencyToken = true;
            }
        }

    }
}