using Consola.Models;
using Consola.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Consola.Pages;

public sealed partial class ServicesPage : Page
{
    private List<ServiceRow> _all = [];

    public ServicesPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        SessionState.Current.PropertyChanged += OnSession;
        Refresh();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        SessionState.Current.PropertyChanged -= OnSession;
    }

    private void OnSession(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SessionState.SelectedProcess) or null) Apply();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => Refresh();

    private async void Refresh()
    {
        CountText.Text = "Leyendo servicios…";
        _all = await Task.Run(() => WindowsServiceMonitor.Instance.Refresh());
        Apply();
    }

    private void Apply()
    {
        var q = SearchBox.Text?.Trim() ?? "";
        IEnumerable<ServiceRow> rows = _all;
        var target = SessionState.Current.SelectedProcess;
        if (target != null)
            rows = rows.Where(s => Matches(s, target));

        if (!string.IsNullOrEmpty(q))
        {
            rows = rows.Where(s =>
                s.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                s.DisplayName.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                s.Path.Contains(q, StringComparison.OrdinalIgnoreCase));
        }

        var list = rows.ToList();
        ServiceList.ItemsSource = list;

        if (target == null)
        {
            CountText.Text = $"{list.Count} servicios · {_all.Count(s => s.Status == "En ejecución")} en ejecución";
            EmptyBar.IsOpen = true;
            EmptyBar.Title = "Sin proceso seleccionado";
            EmptyBar.Message = "Elige un programa arriba. Si es un servicio (o svchost), aquí aparecerán las coincidencias.";
            EmptyBar.Severity = InfoBarSeverity.Informational;
        }
        else if (list.Count == 0)
        {
            CountText.Text = $"{target.Name} ({target.Pid}) no está registrado como servicio";
            EmptyBar.IsOpen = true;
            EmptyBar.Title = "No es un servicio de Windows";
            EmptyBar.Message = $"{target.Name} no coincide con ningún servicio por PID ni por ruta de ejecutable.";
            EmptyBar.Severity = InfoBarSeverity.Informational;
        }
        else
        {
            CountText.Text = $"{list.Count} servicio(s) de {target.Name} ({target.Pid})";
            EmptyBar.IsOpen = false;
        }
    }

    private static bool Matches(ServiceRow service, ProcessRow target)
    {
        if (service.Pid != 0 && service.Pid == target.Pid) return true;
        var file = Path.GetFileName(target.Path);
        if (!string.IsNullOrWhiteSpace(target.Path) &&
            service.Path.Contains(target.Path, StringComparison.OrdinalIgnoreCase))
            return true;
        if (!string.IsNullOrWhiteSpace(file) &&
            service.Path.Contains(file, StringComparison.OrdinalIgnoreCase))
            return true;
        return false;
    }

    private void Search_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args) => Apply();

    private async void Start_Click(object sender, RoutedEventArgs e) => await Control("start");
    private async void Stop_Click(object sender, RoutedEventArgs e) => await Control("stop");
    private async void Restart_Click(object sender, RoutedEventArgs e) => await Control("restart");

    private async Task Control(string action)
    {
        if (ServiceList.SelectedItem is not ServiceRow row) return;
        var result = await Task.Run(() => WindowsServiceMonitor.Instance.Control(row.Name, action));
        await App.ShowErrorAsync("Servicio", result);
        Refresh();
    }
}
