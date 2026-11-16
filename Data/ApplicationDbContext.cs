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

        // MC_STOCK_COUNT_APPROVAL_2026
        builder.Entity<RawStockCountApproval>(e =>
        {
            e.ToTable("RawStockCountApprovals");

            e.HasKey(x => x.Id);

            e.HasIndex(x => x.RawStockCountId)
                .IsUnique();

            e.HasIndex(x => x.StockMovementId)
                .IsUnique();

            e.HasOne(x => x.RawStockCount)
                .WithMany()
                .HasForeignKey(x => x.RawStockCountId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(x => x.StockMovement)
                .WithMany()
                .HasForeignKey(x => x.StockMovementId)
                .OnDelete(DeleteBehavior.Restrict);
        });



        // MC_PRODUCTION_PACKAGING_LINK_2026
        builder.Entity<ProductionPackagingLink>(e =>
        {
            e.ToTable("ProductionPackagingLinks");

            e.HasKey(x => x.Id);

            e.HasIndex(x => x.PackagingBatchId)
                .IsUnique();

            e.HasIndex(x => x.ProductionOrderId);

            e.HasIndex(x => x.ProductLotId);

            e.HasOne(x => x.PackagingBatch)
                .WithMany()
                .HasForeignKey(x => x.PackagingBatchId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(x => x.ProductionOrder)
                .WithMany()
                .HasForeignKey(x => x.ProductionOrderId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(x => x.ProductLot)
                .WithMany()
                .HasForeignKey(x => x.ProductLotId)
                .OnDelete(DeleteBehavior.Restrict);
        });



        // MC_RAW_STOCK_COUNT_2026
        builder.Entity<RawStockCount>(e =>
        {
            e.ToTable("RawStockCounts");
            e.HasKey(x => x.Id);

            e.HasIndex(x => x.CountNumber).IsUnique();

            e.HasIndex(x => new
            {
                x.WarehouseId,
                x.RawMaterialId,
                x.CreatedAtUtc
            });

            e.Property(x => x.SystemQuantity).HasPrecision(18, 4);
            e.Property(x => x.CountedQuantity).HasPrecision(18, 4);
            e.Property(x => x.Difference).HasPrecision(18, 4);

            e.HasOne(x => x.RawMaterial)
                .WithMany()
                .HasForeignKey(x => x.RawMaterialId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(x => x.Warehouse)
                .WithMany()
                .HasForeignKey(x => x.WarehouseId)
                .OnDelete(DeleteBehavior.Restrict);
        });



        // MC_LOT_QUALITY_MANAGEMENT_2026
        builder.Entity<ProductLot>(e =>
        {
            e.ToTable("ProductLots");

            e.HasKey(x => x.Id);

            e.HasIndex(x => new
            {
                x.ProductId,
                x.LotNumber
            }).IsUnique();

            e.HasIndex(x => x.ExpiryDate);

            e.HasIndex(x => x.Status);

            e.HasOne(x => x.Product)
                .WithMany()
                .HasForeignKey(x => x.ProductId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasMany(x => x.Inspections)
                .WithOne(x => x.ProductLot)
                .HasForeignKey(x => x.ProductLotId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<ProductQualityInspection>(e =>
        {
            e.ToTable("ProductQualityInspections");

            e.HasKey(x => x.Id);

            e.HasIndex(x => new
            {
                x.ProductLotId,
                x.InspectedAtUtc
            });
        });



        // MC_PRODUCTION_MANAGEMENT_2026
        builder.Entity<ProductionRecipe>(e =>
        {
            e.ToTable("ProductionRecipes");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.BatchQuantityKg).HasPrecision(18, 4);

            e.HasOne(x => x.Product)
                .WithMany()
                .HasForeignKey(x => x.ProductId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<ProductionRecipeLine>(e =>
        {
            e.ToTable("ProductionRecipeLines");
            e.HasKey(x => x.Id);

            e.HasIndex(x => new
            {
                x.ProductionRecipeId,
                x.RawMaterialId
            }).IsUnique();

            e.Property(x => x.Quantity).HasPrecision(18, 4);

            e.HasOne(x => x.Recipe)
                .WithMany(x => x.Lines)
                .HasForeignKey(x => x.ProductionRecipeId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(x => x.RawMaterial)
                .WithMany()
                .HasForeignKey(x => x.RawMaterialId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<ProductionOrder>(e =>
        {
            e.ToTable("ProductionOrders");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.OrderNumber).IsUnique();

            e.Property(x => x.PlannedQuantityKg).HasPrecision(18, 4);

            e.HasOne(x => x.Recipe)
                .WithMany()
                .HasForeignKey(x => x.ProductionRecipeId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(x => x.Warehouse)
                .WithMany()
                .HasForeignKey(x => x.WarehouseId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<ProductionConsumption>(e =>
        {
            e.ToTable("ProductionConsumptions");
            e.HasKey(x => x.Id);

            e.HasIndex(x => new
            {
                x.ProductionOrderId,
                x.RawMaterialId
            }).IsUnique();

            e.HasIndex(x => x.RawMaterialStockMovementId)
                .IsUnique();

            e.Property(x => x.Quantity).HasPrecision(18, 4);

            e.HasOne(x => x.ProductionOrder)
                .WithMany(x => x.Consumptions)
                .HasForeignKey(x => x.ProductionOrderId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(x => x.RawMaterial)
                .WithMany()
                .HasForeignKey(x => x.RawMaterialId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(x => x.Movement)
                .WithMany()
                .HasForeignKey(x => x.RawMaterialStockMovementId)
                .OnDelete(DeleteBehavior.Restrict);
        });



        // MC_RAW_RECEIPT_TRANSFER_2026
        builder.Entity<RawMaterialReceiptTransfer>(e =>
        {
            e.ToTable("RawMaterialReceiptTransfers");

            e.HasKey(x => x.Id);

            e.HasIndex(x => x.PurchaseGoodsReceiptLineId)
                .IsUnique();

            e.HasIndex(x => x.RawMaterialStockMovementId)
                .IsUnique();

            e.HasIndex(x => x.RawMaterialId);

            e.HasIndex(x => x.WarehouseId);

            e.Property(x => x.Quantity)
                .HasPrecision(18, 4);

            e.HasOne(x => x.PurchaseGoodsReceiptLine)
                .WithMany()
                .HasForeignKey(x => x.PurchaseGoodsReceiptLineId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(x => x.RawMaterial)
                .WithMany()
                .HasForeignKey(x => x.RawMaterialId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(x => x.Warehouse)
                .WithMany()
                .HasForeignKey(x => x.WarehouseId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(x => x.RawMaterialStockMovement)
                .WithMany()
                .HasForeignKey(x => x.RawMaterialStockMovementId)
                .OnDelete(DeleteBehavior.Restrict);
        });



        // MC_RAW_MATERIAL_MANAGEMENT_2026
        builder.Entity<RawMaterial>(e =>
        {
            e.ToTable("RawMaterials");

            e.HasKey(x => x.Id);

            e.HasIndex(x => x.Code).IsUnique();

            e.Property(x => x.MinimumStock)
                .HasPrecision(18, 4);
        });

        builder.Entity<RawMaterialWarehouse>(e =>
        {
            e.ToTable("RawMaterialWarehouses");

            e.HasKey(x => x.Id);

            e.HasIndex(x => x.Code).IsUnique();
        });

        builder.Entity<RawMaterialStockMovement>(e =>
        {
            e.ToTable("RawMaterialStockMovements");

            e.HasKey(x => x.Id);

            e.HasIndex(x => x.OperationNumber).IsUnique();

            e.HasIndex(x => new
            {
                x.RawMaterialId,
                x.WarehouseId,
                x.CreatedAtUtc
            });

            e.Property(x => x.Quantity).HasPrecision(18, 4);

            e.HasOne(x => x.RawMaterial)
                .WithMany()
                .HasForeignKey(x => x.RawMaterialId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(x => x.Warehouse)
                .WithMany()
                .HasForeignKey(x => x.WarehouseId)
                .OnDelete(DeleteBehavior.Restrict);
        });



        // MC_GOODS_STOCK_TRANSFER_2026
        builder.Entity<GoodsReceiptStockTransfer>(e =>
        {
            e.ToTable("GoodsReceiptStockTransfers");
            e.HasKey(x => x.Id);

            // Her mal kabul kalemi yalnizca bir kez aktarilir.
            e.HasIndex(x => x.PurchaseGoodsReceiptLineId)
                .IsUnique();

            e.HasIndex(x => x.StockMovementId)
                .IsUnique();

            e.HasIndex(x => x.PalletItemId)
                .IsUnique();

            e.HasOne(x => x.PurchaseGoodsReceiptLine)
                .WithMany()
                .HasForeignKey(x => x.PurchaseGoodsReceiptLineId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(x => x.Product)
                .WithMany()
                .HasForeignKey(x => x.ProductId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(x => x.Pallet)
                .WithMany()
                .HasForeignKey(x => x.PalletId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(x => x.PalletItem)
                .WithMany()
                .HasForeignKey(x => x.PalletItemId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(x => x.StockMovement)
                .WithMany()
                .HasForeignKey(x => x.StockMovementId)
                .OnDelete(DeleteBehavior.Restrict);
        });



        // MC_GOODS_RECEIPT_2026
        builder.Entity<PurchaseGoodsReceipt>(e =>
        {
            e.ToTable("PurchaseGoodsReceipts");
            e.HasKey(x => x.Id);

            e.HasIndex(x => x.ReceiptNumber).IsUnique();

            e.HasIndex(x => x.PurchaseDispatchNoteId);

            e.HasOne(x => x.PurchaseDispatchNote)
                .WithMany()
                .HasForeignKey(x => x.PurchaseDispatchNoteId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasMany(x => x.Lines)
                .WithOne(x => x.PurchaseGoodsReceipt)
                .HasForeignKey(x => x.PurchaseGoodsReceiptId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<PurchaseGoodsReceiptLine>(e =>
        {
            e.ToTable("PurchaseGoodsReceiptLines");
            e.HasKey(x => x.Id);

            e.HasIndex(x => new
            {
                x.PurchaseGoodsReceiptId,
                x.PurchaseDispatchLineId
            }).IsUnique();

            e.HasOne(x => x.PurchaseDispatchLine)
                .WithMany()
                .HasForeignKey(x => x.PurchaseDispatchLineId)
                .OnDelete(DeleteBehavior.Restrict);

            e.Property(x => x.ReceivedQuantity).HasPrecision(18, 4);
            e.Property(x => x.AcceptedQuantity).HasPrecision(18, 4);
            e.Property(x => x.RejectedQuantity).HasPrecision(18, 4);
        });



        // MC_PURCHASE_DISPATCH_2026
        builder.Entity<PurchaseDispatchNote>(e =>
        {
            e.ToTable("PurchaseDispatchNotes");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.DocumentNumber).IsUnique();
            e.HasIndex(x => x.PurchaseOrderId);
            e.HasIndex(x => x.SupplierId);

            e.HasOne(x => x.PurchaseOrder)
                .WithMany()
                .HasForeignKey(x => x.PurchaseOrderId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(x => x.Supplier)
                .WithMany()
                .HasForeignKey(x => x.SupplierId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasMany(x => x.Lines)
                .WithOne(x => x.PurchaseDispatchNote)
                .HasForeignKey(x => x.PurchaseDispatchNoteId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<PurchaseDispatchLine>(e =>
        {
            e.ToTable("PurchaseDispatchLines");
            e.HasKey(x => x.Id);

            e.HasIndex(x => new
            {
                x.PurchaseDispatchNoteId,
                x.LineNumber
            }).IsUnique();

            e.HasOne(x => x.PurchaseOrderLine)
                .WithMany()
                .HasForeignKey(x => x.PurchaseOrderLineId)
                .OnDelete(DeleteBehavior.Restrict);

            e.Property(x => x.Quantity).HasPrecision(18, 4);
        });



        // MC_PURCHASING_2026
        builder.Entity<PurchaseOrder>(e =>
        {
            e.ToTable("PurchaseOrders");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.OrderNumber).IsUnique();
            e.HasIndex(x => x.SupplierId);
            e.Property(x => x.OrderDate).IsRequired();

            e.HasOne(x => x.Supplier)
                .WithMany()
                .HasForeignKey(x => x.SupplierId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasMany(x => x.Lines)
                .WithOne(x => x.PurchaseOrder)
                .HasForeignKey(x => x.PurchaseOrderId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<PurchaseOrderLine>(e =>
        {
            e.ToTable("PurchaseOrderLines");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new
            {
                x.PurchaseOrderId,
                x.LineNumber
            }).IsUnique();

            e.Property(x => x.Quantity).HasPrecision(18, 4);
            e.Property(x => x.UnitPrice).HasPrecision(18, 4);
            e.Property(x => x.VatRate).HasPrecision(5, 2);
        });



        // MC_CARI_RELATED_2026

        builder.Entity<CariBankAccount>(e =>
        {
            e.ToTable("CariBankAccounts");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.CariAccountId);
            e.HasOne<CariAccount>()
                .WithMany()
                .HasForeignKey(x => x.CariAccountId)
                .OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.ConcurrencyStamp).IsConcurrencyToken();
        });

        builder.Entity<CariContact>(e =>
        {
            e.ToTable("CariContacts");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.CariAccountId);
            e.HasOne<CariAccount>()
                .WithMany()
                .HasForeignKey(x => x.CariAccountId)
                .OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.ConcurrencyStamp).IsConcurrencyToken();
        });

        builder.Entity<CariAddress>(e =>
        {
            e.ToTable("CariAddresses");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.CariAccountId);
            e.HasOne<CariAccount>()
                .WithMany()
                .HasForeignKey(x => x.CariAccountId)
                .OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.ConcurrencyStamp).IsConcurrencyToken();
        });

        builder.Entity<CariAuditEvent>(e =>
        {
            e.ToTable("CariAuditEvents");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.CariAccountId, x.CreatedAtUtc });
            e.HasOne<CariAccount>()
                .WithMany()
                .HasForeignKey(x => x.CariAccountId)
                .OnDelete(DeleteBehavior.Restrict);
        });


        // MC_CARI_ACCOUNTS_2026
        builder.Entity<CariAccount>(entity =>
        {
            entity.ToTable("CariAccounts");

            entity.HasKey(x => x.Id);

            entity.HasIndex(x => x.Code)
                .IsUnique();

            entity.HasIndex(x => x.CompanyName);

            entity.HasIndex(x => x.TaxNumber);

            entity.Property(x => x.Code)
                .IsRequired()
                .HasMaxLength(30);

            entity.Property(x => x.CompanyName)
                .IsRequired()
                .HasMaxLength(250);

            entity.Property(x => x.Currency)
                .IsRequired()
                .HasMaxLength(3);

            entity.Property(x => x.ConcurrencyStamp)
                .IsConcurrencyToken();
        });


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