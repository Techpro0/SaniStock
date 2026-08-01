using System.Windows;
using System.Windows.Threading;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using SaniStock.App.Infrastructure;
using SaniStock.App.ViewModels;
using SaniStock.App.Views;
using SaniStock.Data;
using SaniStock.Domain;
using SaniStock.Reports;

namespace SaniStock.App;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = default!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        AppPaths.EnsureDirectories();

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(System.IO.Path.Combine(AppPaths.LogDir, "log-.txt"),
                rollingInterval: RollingInterval.Day, retainedFileCountLimit: 14)
            .CreateLogger();

        PdfReports.EnsureLicense();
        RegisterGlobalExceptionHandlers();

        Services = BuildServices();

        try
        {
            InitializeDatabase();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Database initialization failed");
            MessageBox.Show("SaniStock could not open its database:\n\n" + ex.Message,
                "Startup error", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
            return;
        }

        ShowLogin();
    }

    private static IServiceProvider BuildServices()
    {
        var services = new ServiceCollection();

        services.AddSingleton<UserContext>();
        services.AddSingleton<IUserContext>(sp => sp.GetRequiredService<UserContext>());

        services.AddDbContextFactory<SaniStockDbContext>(o => o.UseSqlite(AppPaths.ConnectionString));
        services.AddSingleton<IDomainScopeFactory, DomainScopeFactory>();
        services.AddSingleton<IDialogService, DialogService>();

        // Screens (fresh instance per navigation so data reloads cleanly)
        services.AddTransient<DashboardViewModel>();
        services.AddTransient<ProductionViewModel>();
        services.AddTransient<PackingViewModel>();
        services.AddTransient<StockViewModel>();
        services.AddTransient<OrderBookingViewModel>();
        services.AddTransient<OrdersViewModel>();
        services.AddTransient<OrderDispatchViewModel>();
        services.AddTransient<ReportsViewModel>();
        services.AddTransient<MasterDataViewModel>();
        services.AddTransient<UserManagementViewModel>();
        services.AddTransient<BackupViewModel>();
        services.AddTransient<AboutViewModel>();

        services.AddTransient<ShellViewModel>();
        services.AddTransient<LoginViewModel>();
        services.AddTransient<LoginWindow>();
        services.AddTransient<ShellWindow>();

        return services.BuildServiceProvider();
    }

    private static void InitializeDatabase()
    {
        var factory = Services.GetRequiredService<IDbContextFactory<SaniStockDbContext>>();
        using var db = factory.CreateDbContext();
        db.Database.Migrate();
        DbSeeder.EnsureSeeded(db);
        Log.Information("Database ready at {Path}", AppPaths.DbPath);
    }

    private void ShowLogin()
    {
        var login = Services.GetRequiredService<LoginWindow>();
        login.Show();
    }

    private void RegisterGlobalExceptionHandlers()
    {
        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error(args.Exception, "Unhandled UI exception");
            MessageBox.Show("Something went wrong:\n\n" + args.Exception.Message,
                "SaniStock", MessageBoxButton.OK, MessageBoxImage.Warning);
            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Log.Fatal(args.ExceptionObject as Exception, "Unhandled non-UI exception");

        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error(args.Exception, "Unobserved task exception");
            args.SetObserved();
        };
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.CloseAndFlush();
        base.OnExit(e);
    }
}
