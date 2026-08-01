namespace SaniStock.Data.Entities;

/// <summary>
/// Master data that can be switched off instead of removed. Every list in Setup Lists implements
/// this, which is what lets one generic list controller offer Delete and Restore for all of them.
/// <para>
/// <b>Nothing in this application is ever physically deleted.</b> "Delete" in the UI clears
/// <see cref="IsActive"/>: the row and everything referring to it — stock balances, ledger
/// movements, historic orders — stay exactly as they were, and Restore sets the flag back. A
/// deactivated record simply stops being offered in the dropdowns for new work.
/// </para>
/// One flag carries both meanings deliberately. "Temporarily deactivated" and "deleted" are the
/// same state here: the record is out of use but its history is intact.
/// </summary>
public interface IActivatable
{
    bool IsActive { get; set; }
}
