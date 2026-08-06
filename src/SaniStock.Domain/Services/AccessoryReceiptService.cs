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

        _stock.ApplyAccessory(input.AccessoryId, brandId: null, StockMovementType.Production,
            deltaRawOnHand: input.Quantity, deltaPackedOnHand: 0, deltaReserved: 0,
            input.Date, "AccessoryReceipt", receipt.Id, input.Remarks);
        _db.SaveChanges();
        return receipt;
    }

    /// <summary>
    /// Reverses a receipt by taking the same quantity back out of the unpacked pool and recording a
    /// linked reversing entry. The original row is left untouched (immutable).
    /// <para>
    /// Blocked once the received quantity has been packed: a receipt only ever lands unpacked, so
    /// undoing it draws on the unpacked balance the same way undoing a finished-goods production
    /// entry does. This mirrors the guard <see cref="ProductionService.Reverse"/> applies.
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

        var rawOnHand = _stock.FindAccessoryBalance(original.AccessoryId)?.RawOnHand ?? 0m;
        if (rawOnHand < original.Quantity)
            throw new DomainException(
                $"This receipt of {original.Quantity:0.###} cannot be reversed: only {rawOnHand:0.###} " +
                $"of {AccessoryName(original.AccessoryId)} is still unpacked. Undo the packing for this " +
                "accessory first.");

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

        _stock.ApplyAccessory(original.AccessoryId, brandId: null, StockMovementType.Adjustment,
            deltaRawOnHand: -original.Quantity, deltaPackedOnHand: 0, deltaReserved: 0,
            reversal.Date, "AccessoryReceipt", reversal.Id, reversal.Remarks);
        _db.SaveChanges();
        return reversal;
    }

    private string AccessoryName(int accessoryId) =>
        _db.Accessories.Where(a => a.Id == accessoryId).Select(a => a.Name).FirstOrDefault() ?? "this accessory";
}
