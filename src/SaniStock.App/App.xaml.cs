using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using SaniStock.App.Infrastructure;
using SaniStock.App.ViewModels;
using SaniStock.App.Views;
using SaniStock.Data;
using SaniStock.Domain;
using SaniStock.Domain.Services;
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
        RegisterCopyableTextSupport();

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

        // Only an upgrade — a database that already exists and has migrations still to apply —
        // is ever at risk of a schema change touching existing rows, so this is also the only
        // moment a safety backup is worth the cost of making one.
        var pending = db.Database.GetPendingMigrations().ToList();
        if (pending.Count > 0 && File.Exists(AppPaths.DbPath))
            BackUpBeforeMigrating(pending);

        db.Database.Migrate();
        DbSeeder.EnsureSeeded(db);
        Log.Information("Database ready at {Path}", AppPaths.DbPath);
    }

    /// <summary>
    /// Copies the database aside before <see cref="InitializeDatabase"/> applies pending migrations,
    /// independent of the installer's own pre-install backup — this fires for every deployment
    /// method, not only an install through <c>SaniStock-Setup.exe</c>. Client-entered master data
    /// (items, accessories, product types, orders, …) is never touched by <see cref="DbSeeder"/>
    /// itself — it only ever adds rows that do not already exist — but a schema migration can and
    /// occasionally does rewrite existing rows (see <c>AddBrand</c>, <c>AddAccessoryBranding</c>),
    /// so this is the safety net for that case specifically.
    /// </summary>
    private static void BackUpBeforeMigrating(IReadOnlyList<string> pendingMigrations)
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "unknown";
        Log.Information("Applying {Count} pending migration(s) for version {Version}: {Migrations}",
            pendingMigrations.Count, version, string.Join(", ", pendingMigrations));

        try
        {
            var backedUp = new BackupService(AppPaths.DbPath).BackUpBeforeUpgrade(AppPaths.BackupDir, version);
            foreach (var path in backedUp) Log.Information("Safety backup written: {Path}", path);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not make a safety backup before upgrading the database");
            var proceed = MessageBox.Show(
                "SaniStock could not make a safety copy of your existing data before applying this " +
                "update.\n\nYour data will not be changed unless you choose to continue. It is safe " +
                "to say No and copy %LOCALAPPDATA%\\SaniStock\\sanistock.db elsewhere by hand first.\n\n" +
                "Continue with the update anyway?",
                "Safety backup failed", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;

            if (!proceed)
                throw new InvalidOperationException(
                    "Update cancelled: no safety backup could be made of your existing data.", ex);
        }
    }

    private void ShowLogin()
    {
        var login = Services.GetRequiredService<LoginWindow>();
        login.Show();
    }

    /// <summary>
    /// Lets any read-only <see cref="TextBlock"/> in the app be copied via its right-click "Copy"
    /// menu item (see the base TextBlock style in Themes/Styles.xaml). Registered once, class-wide,
    /// rather than per-instance, so every label/value shown anywhere in the app is copyable without
    /// making read-only text focusable just to receive Ctrl+C.
    /// </summary>
    private static void RegisterCopyableTextSupport()
    {
        CommandManager.RegisterClassCommandBinding(typeof(TextBlock), new CommandBinding(
            ApplicationCommands.Copy,
            (sender, args) =>
            {
                if (sender is TextBlock { Text: { Length: > 0 } text })
                {
                    try { Clipboard.SetText(text); }
                    catch (Exception ex) { Log.Warning(ex, "Copy to clipboard failed"); }
                }
                args.Handled = true;
            },
            (sender, args) => args.CanExecute = sender is TextBlock { Text.Length: > 0 }));
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
