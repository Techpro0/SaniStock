using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SaniStock.Data;

/// <summary>
/// Enables <c>dotnet ef migrations</c> to build the context without running the WPF app.
/// The connection string here is only used at design time.
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<SaniStockDbContext>
{
    public SaniStockDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<SaniStockDbContext>()
            .UseSqlite("Data Source=sanistock_designtime.db")
            .Options;
        return new SaniStockDbContext(options);
    }
}
