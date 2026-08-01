using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.EntityFrameworkCore;
using SaniStock.App.Infrastructure;
using SaniStock.Data.Entities;
using SaniStock.Domain;

namespace SaniStock.App.ViewModels;

/// <summary>One selectable accessory row in the Item setup's "default accessories" editor.</summary>
public partial class AccessoryDefaultRow : ObservableObject
{
    public int AccessoryId { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private string _qtyPerUnit = "1";
}

/// <summary>Admin-only CRUD for all master data, one <see cref="MasterList{T}"/> per entity.</summary>
public partial class MasterDataViewModel : ViewModelBase
{
    private readonly IDomainScopeFactory _scopes;
    private readonly IDialogService _dialogs;

    public MasterList<ProductType> ProductTypesTab { get; }
    public MasterList<Item> ItemsTab { get; }
    public MasterList<Grade> GradesTab { get; }
    public MasterList<Colour> ColoursTab { get; }
    public MasterList<Brand> BrandsTab { get; }
    public MasterList<Accessory> AccessoriesTab { get; }
    public MasterList<Party> PartiesTab { get; }
    public MasterList<RawMaterial> RawMaterialsTab { get; }

    /// <summary>Product-type choices for the Items dropdown, led by a 0-id "(None)" sentinel.</summary>
    public ObservableCollection<ProductType> ProductTypeOptions { get; } = new();

    /// <summary>Default-accessory rows for the item currently being edited on the Items tab.</summary>
    public ObservableCollection<AccessoryDefaultRow> AccessoryDefaults { get; } = new();

    /// <summary>When off, the item is saved with no default accessories (recipe cleared).</summary>
    [ObservableProperty] private bool _includeAccessoriesByDefault;

    public MasterDataViewModel(IDomainScopeFactory scopes, IDialogService dialogs)
    {
        _scopes = scopes;
        _dialogs = dialogs;
        Title = "Master Data";
        void Err(string m) => _dialogs.Error(m);

        ProductTypesTab = new MasterList<ProductType>(
            () => Query(db => db.ProductTypes.OrderBy(x => x.Name).ToList()),
            e => Do(s => s.Master.SaveProductType(e)),
            e => new ProductType { Id = e.Id, Code = e.Code, Name = e.Name, IsActive = e.IsActive },
            () => new ProductType { IsActive = true }, Err);

        ItemsTab = new MasterList<Item>(
            () => Query(db => db.Items.Include(x => x.ProductType).OrderBy(x => x.Name).ToList()),
            e => Do(s => { s.Master.SaveItem(e); PersistItemAccessoryDefaults(s, e.Id); }),
            e => new Item { Id = e.Id, Code = e.Code, Name = e.Name, UnitOfMeasure = e.UnitOfMeasure, IsActive = e.IsActive, ProductTypeId = e.ProductTypeId },
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

        BrandsTab = new MasterList<Brand>(
            () => Query(db => db.Brands.OrderBy(x => x.Name).ToList()),
            e => Do(s => s.Master.SaveBrand(e)),
            e => new Brand { Id = e.Id, Code = e.Code, Name = e.Name, IsActive = e.IsActive },
            () => new Brand { IsActive = true }, Err);

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

        // Keep the Items tab's product-type dropdown in sync as types are added/edited.
        ProductTypesTab.Items.CollectionChanged += (_, _) => RebuildProductTypeOptions();

        // Reload the default-accessory editor whenever the edited item changes (New / Edit / post-save).
        ItemsTab.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MasterList<Item>.Editing)) LoadItemAccessoryDefaults(ItemsTab.Editing);
        };
    }

    public override void OnActivated()
    {
        ProductTypesTab.Reload();
        ItemsTab.Reload();
        GradesTab.Reload();
        ColoursTab.Reload();
        BrandsTab.Reload();
        AccessoriesTab.Reload();
        PartiesTab.Reload();
        RawMaterialsTab.Reload();
        LoadItemAccessoryDefaults(ItemsTab.Editing);
    }

    private void RebuildProductTypeOptions()
    {
        ProductTypeOptions.Clear();
        ProductTypeOptions.Add(new ProductType { Id = 0, Name = "(None)" });
        foreach (var pt in ProductTypesTab.Items) ProductTypeOptions.Add(pt);
    }

    /// <summary>
    /// Rebuilds the default-accessory checklist for the given item: every active accessory (plus
    /// any inactive one already in the recipe), pre-ticked with its saved qty-per-unit.
    /// </summary>
    private void LoadItemAccessoryDefaults(Item? item)
    {
        AccessoryDefaults.Clear();
        using var scope = _scopes.Create();

        var existing = item is { Id: > 0 }
            ? scope.Master.GetItemAccessoryDefaults(item.Id).ToDictionary(d => d.AccessoryId, d => d.QtyPerUnit)
            : new Dictionary<int, decimal>();
        var existingIds = existing.Keys.ToList();

        var accessories = scope.Db.Accessories
            .Where(a => a.IsActive || existingIds.Contains(a.Id))
            .OrderBy(a => a.Name).ToList();

        foreach (var a in accessories)
        {
            var selected = existing.TryGetValue(a.Id, out var qty);
            AccessoryDefaults.Add(new AccessoryDefaultRow
            {
                AccessoryId = a.Id,
                Code = a.Code,
                Name = a.Name,
                IsSelected = selected,
                QtyPerUnit = (selected ? qty : 1m).ToString("0.###")
            });
        }
        IncludeAccessoriesByDefault = existing.Count > 0;
    }

    /// <summary>Saves the ticked accessory rows as the item's recipe (or clears it when the toggle is off).</summary>
    private void PersistItemAccessoryDefaults(DomainScope scope, int itemId)
    {
        if (itemId <= 0) return;

        var chosen = new List<(int AccessoryId, decimal QtyPerUnit)>();
        if (IncludeAccessoriesByDefault)
        {
            foreach (var row in AccessoryDefaults.Where(r => r.IsSelected))
            {
                if (!decimal.TryParse(row.QtyPerUnit, out var qty) || qty <= 0)
                    throw new DomainException($"Enter a valid quantity per unit for {row.Name}.");
                chosen.Add((row.AccessoryId, qty));
            }
        }
        scope.Master.SetItemAccessoryDefaults(itemId, chosen);
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
