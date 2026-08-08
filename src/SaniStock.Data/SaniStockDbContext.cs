using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using SaniStock.Data.Entities;

namespace SaniStock.Data;

/// <summary>EF Core context for the SaniStock local SQLite database.</summary>
public class SaniStockDbContext : DbContext
{
    public SaniStockDbContext(DbContextOptions<SaniStockDbContext> options) : base(options) { }

    // Master data
    public DbSet<Item> Items => Set<Item>();
    public DbSet<ProductType> ProductTypes => Set<ProductType>();
    public DbSet<Grade> Grades => Set<Grade>();
    public DbSet<Colour> Colours => Set<Colour>();
    public DbSet<Brand> Brands => Set<Brand>();
    public DbSet<Accessory> Accessories => Set<Accessory>();
    public DbSet<ItemAccessoryDefault> ItemAccessoryDefaults => Set<ItemAccessoryDefault>();
    public DbSet<Party> Parties => Set<Party>();
    public DbSet<RawMaterial> RawMaterials => Set<RawMaterial>();

    // Finished-goods stock
    public DbSet<ProductionEntry> ProductionEntries => Set<ProductionEntry>();
    public DbSet<PackingEntry> PackingEntries => Set<PackingEntry>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();
    public DbSet<StockBalance> StockBalances => Set<StockBalance>();

    // Accessory stock
    public DbSet<AccessoryReceipt> AccessoryReceipts => Set<AccessoryReceipt>();
    public DbSet<AccessoryPackingEntry> AccessoryPackingEntries => Set<AccessoryPackingEntry>();
    public DbSet<AccessoryStockMovement> AccessoryStockMovements => Set<AccessoryStockMovement>();
    public DbSet<AccessoryStockBalance> AccessoryStockBalances => Set<AccessoryStockBalance>();

    // Green ware & raw material (optional modules)
    public DbSet<GreenPieceEntry> GreenPieceEntries => Set<GreenPieceEntry>();
    public DbSet<GreenPieceBalance> GreenPieceBalances => Set<GreenPieceBalance>();
    public DbSet<RawMaterialEntry> RawMaterialEntries => Set<RawMaterialEntry>();
    public DbSet<RawMaterialBalance> RawMaterialBalances => Set<RawMaterialBalance>();

    // Orders & dispatch
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderLine> OrderLines => Set<OrderLine>();
    public DbSet<OrderLineAllocation> OrderLineAllocations => Set<OrderLineAllocation>();
    public DbSet<OrderAccessoryLine> OrderAccessoryLines => Set<OrderAccessoryLine>();
    public DbSet<OrderAccessoryLineAllocation> OrderAccessoryLineAllocations => Set<OrderAccessoryLineAllocation>();
    public DbSet<DispatchEntry> DispatchEntries => Set<DispatchEntry>();
    public DbSet<DispatchLine> DispatchLines => Set<DispatchLine>();
    public DbSet<DispatchAccessoryLine> DispatchAccessoryLines => Set<DispatchAccessoryLine>();

    // System
    public DbSet<User> Users => Set<User>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        // Unique business keys
        b.Entity<Item>().HasIndex(x => x.Code).IsUnique();
        b.Entity<ProductType>().HasIndex(x => x.Code).IsUnique();
        b.Entity<Accessory>().HasIndex(x => x.Code).IsUnique();
        b.Entity<Grade>().HasIndex(x => x.Name).IsUnique();
        b.Entity<Colour>().HasIndex(x => x.Name).IsUnique();
        b.Entity<Brand>().HasIndex(x => x.Code).IsUnique();
        b.Entity<RawMaterial>().HasIndex(x => x.Name).IsUnique();
        b.Entity<User>().HasIndex(x => x.Username).IsUnique();
        b.Entity<Order>().HasIndex(x => x.OrderNo).IsUnique();
        b.Entity<DispatchEntry>().HasIndex(x => x.DispatchNo).IsUnique();

        // One balance row per stock key. SQLite (like the SQL standard) treats NULLs as distinct
        // in a unique index, so the four-column index alone would happily allow several brand-less
        // rows for the same item+grade+colour. The filtered index below closes that hole; together
        // they guarantee exactly one row per key, brand-less or branded.
        b.Entity<StockBalance>().HasIndex(x => new { x.ItemId, x.GradeId, x.ColourId, x.BrandId }).IsUnique();
        b.Entity<StockBalance>()
            .HasIndex(x => new { x.ItemId, x.GradeId, x.ColourId })
            .IsUnique()
            .HasFilter("\"BrandId\" IS NULL")
            .HasDatabaseName("IX_StockBalances_Item_Grade_Colour_Unbranded");
        // Same NULL-distinctness problem as StockBalance: the plain (AccessoryId, BrandId) index
        // alone would allow several brand-less rows for one accessory, so the filtered index closes
        // that hole the same way it does there.
        b.Entity<AccessoryStockBalance>().HasIndex(x => new { x.AccessoryId, x.BrandId }).IsUnique();
        b.Entity<AccessoryStockBalance>()
            .HasIndex(x => x.AccessoryId)
            .IsUnique()
            .HasFilter("\"BrandId\" IS NULL")
            .HasDatabaseName("IX_AccessoryStockBalances_Accessory_Unbranded");
        b.Entity<GreenPieceBalance>().HasIndex(x => new { x.ItemId, x.ColourId }).IsUnique();
        b.Entity<RawMaterialBalance>().HasIndex(x => x.RawMaterialId).IsUnique();

        // Ledger query indexes
        b.Entity<StockMovement>().HasIndex(x => new { x.ItemId, x.GradeId, x.ColourId, x.BrandId, x.Date });
        b.Entity<AccessoryStockMovement>().HasIndex(x => new { x.AccessoryId, x.BrandId, x.Date });
        b.Entity<PackingEntry>().HasIndex(x => new { x.ItemId, x.GradeId, x.ColourId, x.Date });
        b.Entity<PackingEntry>().HasIndex(x => x.BatchId);
        b.Entity<AccessoryPackingEntry>().HasIndex(x => new { x.AccessoryId, x.Date });
        b.Entity<AccessoryPackingEntry>().HasIndex(x => x.BatchId);

        // Read-only computed properties are not columns
        b.Entity<StockBalance>().Ignore(x => x.OnHand);
        b.Entity<StockBalance>().Ignore(x => x.Available);
        b.Entity<StockMovement>().Ignore(x => x.DeltaOnHand);
        b.Entity<AccessoryStockBalance>().Ignore(x => x.OnHand);
        b.Entity<AccessoryStockBalance>().Ignore(x => x.Available);
        b.Entity<AccessoryStockMovement>().Ignore(x => x.DeltaOnHand);
        b.Entity<OrderLine>().Ignore(x => x.QuantityPending);
        b.Entity<OrderAccessoryLine>().Ignore(x => x.QuantityPending);
        b.Entity<OrderLineAllocation>().Ignore(x => x.QuantityReserved);
        b.Entity<OrderAccessoryLineAllocation>().Ignore(x => x.QuantityReserved);

        // SQLite has no native decimal type: EF stores decimal as TEXT, which makes numeric
        // comparisons (Reserved > OnHand, negative Available), ORDER BY and SUM behave
        // lexicographically / unsupported. Store all quantities as REAL (double) so the engine
        // compares and aggregates them numerically. Quantities here are counts/measures, not
        // currency, so double precision is more than sufficient.
        var toDouble = new ValueConverter<decimal, double>(v => (double)v, v => (decimal)v);
        var toNullableDouble = new ValueConverter<decimal?, double?>(
            v => v.HasValue ? (double)v.Value : (double?)null,
            v => v.HasValue ? (decimal)v.Value : (decimal?)null);

        foreach (var prop in b.Model.GetEntityTypes()
                     .SelectMany(t => t.GetProperties()))
        {
            if (prop.ClrType == typeof(decimal)) prop.SetValueConverter(toDouble);
            else if (prop.ClrType == typeof(decimal?)) prop.SetValueConverter(toNullableDouble);
        }

        // Don't cascade-delete orders when a party is removed; restrict instead
        b.Entity<Order>()
            .HasOne(o => o.Party)
            .WithMany()
            .HasForeignKey(o => o.PartyId)
            .OnDelete(DeleteBehavior.Restrict);

        // An item's product type is optional; clearing/deleting the type leaves the item intact.
        b.Entity<Item>()
            .HasOne(i => i.ProductType)
            .WithMany()
            .HasForeignKey(i => i.ProductTypeId)
            .OnDelete(DeleteBehavior.SetNull);

        // One default-accessory recipe row per item+accessory pair.
        b.Entity<ItemAccessoryDefault>()
            .HasIndex(x => new { x.ItemId, x.AccessoryId }).IsUnique();
        b.Entity<ItemAccessoryDefault>()
            .HasOne(x => x.Item).WithMany().HasForeignKey(x => x.ItemId)
            .OnDelete(DeleteBehavior.Cascade);
        b.Entity<ItemAccessoryDefault>()
            .HasOne(x => x.Accessory).WithMany().HasForeignKey(x => x.AccessoryId)
            .OnDelete(DeleteBehavior.Restrict);

        // Link an auto-attached accessory line back to its parent item line. Order already
        // cascade-deletes both collections, so this extra edge must NOT cascade (SQLite would
        // otherwise reject the multiple-cascade-path); it is purely a reference for grouping.
        b.Entity<OrderAccessoryLine>()
            .HasOne(x => x.SourceOrderLine).WithMany().HasForeignKey(x => x.SourceOrderLineId)
            .OnDelete(DeleteBehavior.NoAction);

        // A line's allocations belong to it and die with it. The grade reference must not cascade:
        // an allocation records which grade's stock was drawn, and that history outlives grade edits.
        b.Entity<OrderLineAllocation>()
            .HasOne(x => x.OrderLine).WithMany(l => l.Allocations).HasForeignKey(x => x.OrderLineId)
            .OnDelete(DeleteBehavior.Cascade);
        b.Entity<OrderLineAllocation>()
            .HasOne(x => x.Grade).WithMany().HasForeignKey(x => x.GradeId)
            .OnDelete(DeleteBehavior.Restrict);
        b.Entity<OrderLineAllocation>().HasIndex(x => new { x.OrderLineId, x.Priority });

        // Accessory line allocations mirror OrderLineAllocation, minus a grade reference to restrict.
        b.Entity<OrderAccessoryLineAllocation>()
            .HasOne(x => x.OrderAccessoryLine).WithMany(l => l.Allocations).HasForeignKey(x => x.OrderAccessoryLineId)
            .OnDelete(DeleteBehavior.Cascade);
        b.Entity<OrderAccessoryLineAllocation>().HasIndex(x => new { x.OrderAccessoryLineId, x.Priority });

        // Brand references never cascade. Brands are deactivate-only master data, and every row
        // pointing at one is history: which brand stock was packed under, which brand an order was
        // placed for. Restrict also keeps SQLite clear of multiple cascade paths, since orders
        // already cascade to their lines and lines to their allocations.
        b.Entity<StockBalance>()
            .HasOne(x => x.Brand).WithMany().HasForeignKey(x => x.BrandId)
            .OnDelete(DeleteBehavior.Restrict);
        b.Entity<StockMovement>()
            .HasOne(x => x.Brand).WithMany().HasForeignKey(x => x.BrandId)
            .OnDelete(DeleteBehavior.Restrict);
        b.Entity<PackingEntry>()
            .HasOne(x => x.Brand).WithMany().HasForeignKey(x => x.BrandId)
            .OnDelete(DeleteBehavior.Restrict);
        b.Entity<OrderLine>()
            .HasOne(x => x.Brand).WithMany().HasForeignKey(x => x.BrandId)
            .OnDelete(DeleteBehavior.Restrict);
        b.Entity<OrderLineAllocation>()
            .HasOne(x => x.Brand).WithMany().HasForeignKey(x => x.BrandId)
            .OnDelete(DeleteBehavior.Restrict);
        b.Entity<AccessoryStockBalance>()
            .HasOne(x => x.Brand).WithMany().HasForeignKey(x => x.BrandId)
            .OnDelete(DeleteBehavior.Restrict);
        b.Entity<AccessoryStockMovement>()
            .HasOne(x => x.Brand).WithMany().HasForeignKey(x => x.BrandId)
            .OnDelete(DeleteBehavior.Restrict);
        b.Entity<AccessoryPackingEntry>()
            .HasOne(x => x.Brand).WithMany().HasForeignKey(x => x.BrandId)
            .OnDelete(DeleteBehavior.Restrict);
        b.Entity<OrderAccessoryLine>()
            .HasOne(x => x.Brand).WithMany().HasForeignKey(x => x.BrandId)
            .OnDelete(DeleteBehavior.Restrict);
        b.Entity<OrderAccessoryLineAllocation>()
            .HasOne(x => x.Brand).WithMany().HasForeignKey(x => x.BrandId)
            .OnDelete(DeleteBehavior.Restrict);
        b.Entity<AccessoryReceipt>()
            .HasOne(x => x.Brand).WithMany().HasForeignKey(x => x.BrandId)
            .OnDelete(DeleteBehavior.Restrict);

        base.OnModelCreating(b);
    }
}
