using SaniStock.Data.Entities;
using SaniStock.Domain;

namespace SaniStock.Domain.Tests;

public class MasterDataCodeGenerationTests
{
    [Fact]
    public void SaveItem_autogenerates_a_code_when_the_new_item_has_no_code()
    {
        using var h = new TestHarness();

        var saved = h.Master.SaveItem(new Item
        {
            Name = "New Item",
            UnitOfMeasure = "PCS",
            IsActive = true,
            ProductTypeId = null
        });

        Assert.StartsWith("IT-", saved.Code);
        Assert.Matches("^IT-\\d{4}$", saved.Code);
    }

    [Fact]
    public void SaveAccessory_autogenerates_a_code_when_the_new_accessory_has_no_code()
    {
        using var h = new TestHarness();

        var saved = h.Master.SaveAccessory(new Accessory
        {
            Name = "New Accessory",
            UnitOfMeasure = "PCS",
            IsActive = true
        });

        Assert.StartsWith("AC-", saved.Code);
        Assert.Matches("^AC-\\d{4}$", saved.Code);
    }

    [Fact]
    public void SaveProductType_autogenerates_a_code_when_the_new_product_type_has_no_code()
    {
        using var h = new TestHarness();

        var saved = h.Master.SaveProductType(new ProductType
        {
            Name = "New Product Type",
            IsActive = true
        });

        Assert.StartsWith("PT-", saved.Code);
        Assert.Matches("^PT-\\d{4}$", saved.Code);
    }
}
