using System.Reflection;

namespace SaniStock.App.ViewModels;

public class AboutViewModel : ViewModelBase
{
    public AboutViewModel()
    {
        Title = "About";
    }

    public string AppName => "SaniStock";
    public string Tagline => "Sanitary Ware Production & Stock Management";
    public string Version =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";
    public string Framework => ".NET 8 · WPF · SQLite";
    public string DataLocation => Infrastructure.AppPaths.RootDir;
}
