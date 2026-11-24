using System.Windows;
using MetroKafeEpicGames.Services;

namespace MetroKafeEpicGames;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += (_, ex) =>
        {
            AppState.AddLog("Beklenmeyen hata: " + ex.Exception.Message, LogKind.Error);
            MessageBox.Show(ex.Exception.Message, "MetroKafe Epic Games", MessageBoxButton.OK, MessageBoxImage.Error);
            ex.Handled = true;
        };

        AppState.Load();
        // Komut satirindan dosya verilirse onu ac
        if (e.Args.Length > 0 && File.Exists(e.Args[0])) AppState.StartupFile = e.Args[0];

        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }
}
