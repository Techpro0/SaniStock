namespace SaniStock.Data.Entities;

/// <summary>Quality grade of finished ware (1st, 2nd, 3rd — seeded, extensible).</summary>
public class Grade
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}
