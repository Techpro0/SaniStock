using SaniStock.App.Infrastructure;
using SaniStock.Data.Entities;

namespace SaniStock.App.ViewModels;

/// <summary>Admin-only CRUD for all master data, one <see cref="MasterList{T}"/> per entity.</summary>
public class MasterDataViewModel : ViewModelBase
{
    private readonly IDomainScopeFactory _scopes;
    private readonly IDialogService _dialogs;

    public MasterList<Item> ItemsTab { get; }
    public MasterList<Grade> GradesTab { get; }
    public MasterList<Colour> ColoursTab { get; }
    public MasterList<Accessory> AccessoriesTab { get; }
    public MasterList<Party> PartiesTab { get; }
    public MasterList<RawMaterial> RawMaterialsTab { get; }

    public MasterDataViewModel(IDomainScopeFactory scopes, IDialogService dialogs)
    {
        _scopes = scopes;
        _dialogs = dialogs;
        Title = "Master Data";
        void Err(string m) => _dialogs.Error(m);

        ItemsTab = new MasterList<Item>(
            () => Query(db => db.Items.OrderBy(x => x.Name).ToList()),
            e => Do(s => s.Master.SaveItem(e)),
            e => new Item { Id = e.Id, Code = e.Code, Name = e.Name, UnitOfMeasure = e.UnitOfMeasure, IsActive = e.IsActive },
            () => new Item { IsActive = true, UnitOfMeasure = "PCS" }, Err);

        GradesTab = new MasterList<Grade>(
            () => Query(db => db.Grades.OrderBy(x => x.SortOrder).ToList()),
            e => Do(s => s.Master.SaveGrade(e)),
            e => new Grade { Id = e.Id, Name = e.Name, SortOrder = e.SortOrder, IsActive = e.IsActive },
            () => new Grade { IsActive = true }, Err);

        ColoursTab = new MasterList<Colour>(
            () => Query(db => db.Colours.OrderBy(x => x.Name).ToList()),
            e => Do(s => s.Master.SaveColour(e)),
            e => new Colour { Id = e.Id, Name = e.Name, HexCode = e.HexCode, IsActive = e.IsActive },
            () => new Colour { IsActive = true }, Err);

        AccessoriesTab = new MasterList<Accessory>(
            () => Query(db => db.Accessories.OrderBy(x => x.Name).ToList()),
            e => Do(s => s.Master.SaveAccessory(e)),
            e => new Accessory { Id = e.Id, Code = e.Code, Name = e.Name, UnitOfMeasure = e.UnitOfMeasure, IsActive = e.IsActive },
            () => new Accessory { IsActive = true, UnitOfMeasure = "PCS" }, Err);

        PartiesTab = new MasterList<Party>(
            () => Query(db => db.Parties.OrderBy(x => x.Name).ToList()),
            e => Do(s => s.Master.SaveParty(e)),
            e => new Party { Id = e.Id, Name = e.Name, Address = e.Address, Contact = e.Contact, Gstin = e.Gstin, IsActive = e.IsActive },
            () => new Party { IsActive = true }, Err);

        RawMaterialsTab = new MasterList<RawMaterial>(
            () => Query(db => db.RawMaterials.OrderBy(x => x.Name).ToList()),
            e => Do(s => s.Master.SaveRawMaterial(e)),
            e => new RawMaterial { Id = e.Id, Name = e.Name, UnitOfMeasure = e.UnitOfMeasure, IsActive = e.IsActive },
            () => new RawMaterial { IsActive = true, UnitOfMeasure = "KG" }, Err);
    }

    public override void OnActivated()
    {
        ItemsTab.Reload();
        GradesTab.Reload();
        ColoursTab.Reload();
        AccessoriesTab.Reload();
        PartiesTab.Reload();
        RawMaterialsTab.Reload();
    }

    private TResult Query<TResult>(Func<Data.SaniStockDbContext, TResult> q)
    {
        using var scope = _scopes.Create();
        return q(scope.Db);
    }

    private void Do(Action<DomainScope> action)
    {
        using var scope = _scopes.Create();
        action(scope);
    }
}
