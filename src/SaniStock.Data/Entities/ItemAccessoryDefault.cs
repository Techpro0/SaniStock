namespace SaniStock.Data.Entities;

/// <summary>
/// The master "recipe" row linking a default accessory to an <see cref="Item"/>: when the item
/// is ordered, this accessory is booked alongside it at <see cref="QtyPerUnit"/> per item unit,
/// unless excluded on that specific order line. Editing this never affects existing orders.
/// </summary>
public class ItemAccessoryDefault
{
    public int Id { get; set; }

    public int ItemId { get; set; }
    public Item? Item { get; set; }

    public int AccessoryId { get; set; }
    public Accessory? Accessory { get; set; }

    /// <summary>Accessories booked per one unit of the item ordered (default 1).</summary>
    public decimal QtyPerUnit { get; set; } = 1;

    public bool IsActive { get; set; } = true;
}
