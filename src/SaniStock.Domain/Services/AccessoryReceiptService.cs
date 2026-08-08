using SaniStock.Data;
using SaniStock.Data.Entities;
using SaniStock.Domain.Models;

namespace SaniStock.Domain.Services;

/// <summary>
/// Records accessory stock received (and reversals). Ordinarily lands in the shared unpacked pool,
/// same as a finished-goods production entry; naming a brand on the input instead lands it directly
/// on that brand's packed stock, for goods that arrive already packaged — an outside import, for
/// instance — so it never needs a separate trip to the Packing screen.
/// </summary>
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
        if (input.BrandId is int brandId && !_db.Brands.Any(b => b.Id == brandId))
            throw new DomainException("Selected brand does not exist.");

        var receipt = new AccessoryReceipt
        {
            Date = input.Date,
            AccessoryId = input.AccessoryId,
            BrandId = input.BrandId,
            Quantity = input.Quantity,
            Remarks = input.Remarks,
            CreatedBy = _user.Username,
            CreatedAt = DateTime.Now
        };
        _db.AccessoryReceipts.Add(receipt);
        _db.SaveChanges();

        if (input.BrandId is int b)
            _stock.ApplyAccessory(input.AccessoryId, b, StockMovementType.Production,
                deltaRawOnHand: 0, deltaPackedOnHand: input.Quantity, deltaReserved: 0,
                input.Date, "AccessoryReceipt", receipt.Id, input.Remarks);
        else
            _stock.ApplyAccessory(input.AccessoryId, brandId: null, StockMovementType.Production,
                deltaRawOnHand: input.Quantity, deltaPackedOnHand: 0, deltaReserved: 0,
                input.Date, "AccessoryReceipt", receipt.Id, input.Remarks);
        _db.SaveChanges();
        return receipt;
    }

    /// <summary>
    /// Reverses a receipt by taking the same quantity back out — from the shared unpacked pool, or
    /// from the brand's packed stock if the receipt named one — and recording a linked reversing
    /// entry. The original row is left untouched (immutable).
    /// <para>
    /// Blocked once the received quantity is no longer there: an ordinary receipt lands unpacked, so
    /// undoing it is blocked once some of it has since been packed, mirroring
    /// <see cref="ProductionService.Reverse"/>. A receipt that named a brand lands packed directly,
    /// so undoing it is blocked once some of it has since shipped instead.
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

        if (original.BrandId is int brandId)
        {
            var packedOnHand = _stock.FindAccessoryBalance(original.AccessoryId, brandId)?.PackedOnHand ?? 0m;
            if (packedOnHand < original.Quantity)
                throw new DomainException(
                    $"This receipt of {original.Quantity:0.###} cannot be reversed: only {packedOnHand:0.###} " +
                    $"of {AccessoryName(original.AccessoryId)} is still packed for {BrandName(brandId)}. " +
                    "Some of it has already gone out.");
        }
        else
        {
            var rawOnHand = _stock.FindAccessoryBalance(original.AccessoryId)?.RawOnHand ?? 0m;
            if (rawOnHand < original.Quantity)
                throw new DomainException(
                    $"This receipt of {original.Quantity:0.###} cannot be reversed: only {rawOnHand:0.###} " +
                    $"of {AccessoryName(original.AccessoryId)} is still unpacked. Undo the packing for this " +
                    "accessory first.");
        }

        var reversal = new AccessoryReceipt
        {
            Date = DateTime.Today,
            AccessoryId = original.AccessoryId,
            BrandId = original.BrandId,
            Quantity = original.Quantity,
            Remarks = remarks ?? $"Reversal of receipt #{original.Id}",
            IsReversal = true,
            ReversesEntryId = original.Id,
            CreatedBy = _user.Username,
            CreatedAt = DateTime.Now
        };
        _db.AccessoryReceipts.Add(reversal);
        _db.SaveChanges();

        if (original.BrandId is int b)
            _stock.ApplyAccessory(original.AccessoryId, b, StockMovementType.Adjustment,
                deltaRawOnHand: 0, deltaPackedOnHand: -original.Quantity, deltaReserved: 0,
                reversal.Date, "AccessoryReceipt", reversal.Id, reversal.Remarks);
        else
            _stock.ApplyAccessory(original.AccessoryId, brandId: null, StockMovementType.Adjustment,
                deltaRawOnHand: -original.Quantity, deltaPackedOnHand: 0, deltaReserved: 0,
                reversal.Date, "AccessoryReceipt", reversal.Id, reversal.Remarks);
        _db.SaveChanges();
        return reversal;
    }

    private string AccessoryName(int accessoryId) =>
        _db.Accessories.Where(a => a.Id == accessoryId).Select(a => a.Name).FirstOrDefault() ?? "this accessory";

    private string BrandName(int brandId) =>
        _db.Brands.Where(b => b.Id == brandId).Select(b => b.Name).FirstOrDefault() ?? "?";
}
