using SaniStock.Data;
using SaniStock.Data.Entities;
using SaniStock.Domain.Models;

namespace SaniStock.Domain.Services;

/// <summary>Records accessory stock received (and reversals), increasing accessory OnHand.</summary>
public class AccessoryReceiptService
{
    private readonly SaniStockDbContext _db;
    private readonly StockService _stock;
    private readonly IUserContext _user;

    public AccessoryReceiptService(SaniStockDbContext db, StockService stock, IUserContext user)
    {
        _db = db;
        _stock = stock;
        _user = user;
    }

    public AccessoryReceipt Post(AccessoryReceiptInput input)
    {
        if (input.Quantity <= 0)
            throw new DomainException("Received quantity must be greater than zero.");

        var receipt = new AccessoryReceipt
        {
            Date = input.Date,
            AccessoryId = input.AccessoryId,
            Quantity = input.Quantity,
            Remarks = input.Remarks,
            CreatedBy = _user.Username,
            CreatedAt = DateTime.Now
        };
        _db.AccessoryReceipts.Add(receipt);
        _db.SaveChanges();

        _stock.ApplyAccessory(input.AccessoryId, StockMovementType.Production,
            deltaOnHand: input.Quantity, deltaReserved: 0,
            input.Date, "AccessoryReceipt", receipt.Id, input.Remarks);
        _db.SaveChanges();
        return receipt;
    }

    /// <summary>
    /// Reverses a receipt by taking the same quantity back out and recording a linked reversing
    /// entry. The original row is left untouched (immutable).
    /// <para>
    /// Blocked once the stock has gone out again: taking back what has already shipped would drive
    /// the accessory's on-hand negative and quietly misstate every later balance. This mirrors the
    /// guard <see cref="ProductionService.Reverse"/> applies to finished ware.
    /// </para>
    /// </summary>
    public AccessoryReceipt Reverse(int receiptId, string? remarks = null)
    {
        var original = _db.AccessoryReceipts.Find(receiptId)
            ?? throw new DomainException("Accessory receipt not found.");
        if (original.IsReversal)
            throw new DomainException("A reversal entry cannot itself be reversed.");
        if (_db.AccessoryReceipts.Any(r => r.ReversesEntryId == receiptId))
            throw new DomainException("This receipt has already been reversed.");

        var onHand = _db.AccessoryStockBalances
            .Where(b => b.AccessoryId == original.AccessoryId)
            .Select(b => (decimal?)b.OnHand).FirstOrDefault() ?? 0m;
        if (onHand < original.Quantity)
            throw new DomainException(
                $"This receipt of {original.Quantity:0.###} cannot be reversed: only {onHand:0.###} " +
                $"of {AccessoryName(original.AccessoryId)} is still in stock. Some of it has already gone out.");

        var reversal = new AccessoryReceipt
        {
            Date = DateTime.Today,
            AccessoryId = original.AccessoryId,
            Quantity = original.Quantity,
            Remarks = remarks ?? $"Reversal of receipt #{original.Id}",
            IsReversal = true,
            ReversesEntryId = original.Id,
            CreatedBy = _user.Username,
            CreatedAt = DateTime.Now
        };
        _db.AccessoryReceipts.Add(reversal);
        _db.SaveChanges();

        _stock.ApplyAccessory(original.AccessoryId, StockMovementType.Adjustment,
            deltaOnHand: -original.Quantity, deltaReserved: 0,
            reversal.Date, "AccessoryReceipt", reversal.Id, reversal.Remarks);
        _db.SaveChanges();
        return reversal;
    }

    private string AccessoryName(int accessoryId) =>
        _db.Accessories.Where(a => a.Id == accessoryId).Select(a => a.Name).FirstOrDefault() ?? "this accessory";
}
