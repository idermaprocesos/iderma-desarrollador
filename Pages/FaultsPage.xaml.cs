using System.Collections.ObjectModel;
using Consola.Models;
using Consola.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Consola.Pages;

public sealed partial class FaultsPage : Page
{
    private readonly ObservableCollection<FaultEvent> _items = [];
    private List<FaultEvent> _all = [];

    public FaultsPage()
    {
        InitializeComponent();
        FaultList.ItemsSource = _items;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        SessionState.Current.PropertyChanged += OnSession;
        FaultMonitorService.Instance.FaultArrived += OnLive;
        _ = ReloadAsync();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        SessionState.Current.PropertyChanged -= OnSession;
        FaultMonitorService.Instance.FaultArrived -= OnLive;
    }

    private void OnSession(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SessionState.SelectedProcess) or null) Apply();
    }

    private void OnLive(FaultEvent fault)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            _all.Insert(0, fault);
            Apply();
        });
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await ReloadAsync();

    private async Task ReloadAsync()
    {
        _all = await Task.Run(() => FaultMonitorService.Instance.LoadRecent());
        Apply();
    }

    private void Search_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args) => Apply();

    private void Apply()
    {
        var q = SearchBox.Text?.Trim() ?? "";
        IEnumerable<FaultEvent> rows = _all;
        var target = SessionState.Current.SelectedProcess;
        if (target != null)
            rows = rows.Where(f => Matches(f, target));

        if (!string.IsNullOrEmpty(q))
        {
            rows = rows.Where(f =>
                f.Message.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                f.Source.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                f.Process.Contains(q, StringComparison.OrdinalIgnoreCase));
        }

        var list = rows.Take(400).ToList();
        _items.Clear();
        foreach (var item in list) _items.Add(item);

        if (target == null)
        {
            EmptyBar.IsOpen = true;
            EmptyBar.Title = "Sin proceso seleccionado";
            EmptyBar.Message = "Elige un programa arriba para ver solo sus fallos de Application/System, WER y .NET Runtime.";
            EmptyBar.Severity = InfoBarSeverity.Informational;
        }
        else if (list.Count == 0)
        {
            EmptyBar.IsOpen = true;
            EmptyBar.Title = $"Sin fallos recientes de {target.Name}";
            EmptyBar.Message = $"No hay eventos de error de {target.Name} ({target.Pid}) en las últimas 24 h.";
            EmptyBar.Severity = InfoBarSeverity.Success;
        }
        else
        {
            EmptyBar.IsOpen = false;
        }
    }

    private static bool Matches(FaultEvent fault, ProcessRow target)
    {
        if (fault.Pid is int pid && pid == target.Pid) return true;
        if (string.IsNullOrWhiteSpace(target.Name)) return false;
        return fault.Process.Contains(target.Name, StringComparison.OrdinalIgnoreCase)
               || fault.Message.Contains(target.Name, StringComparison.OrdinalIgnoreCase);
    }
}
