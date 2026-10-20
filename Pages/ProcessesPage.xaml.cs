using System.Collections.ObjectModel;
using Consola.Helpers;
using Consola.Models;
using Consola.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Consola.Pages;

public sealed partial class ProcessesPage : Page
{
    private readonly ObservableCollection<ProcessRow> _visible = [];
    private bool _suppress;

    public ProcessesPage()
    {
        InitializeComponent();
        ProcessList.ItemsSource = _visible;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        SessionState.Current.PropertyChanged += OnSession;
        MonitorEngine.Instance.Updated += OnUpdated;
        OnUpdated();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        SessionState.Current.PropertyChanged -= OnSession;
        MonitorEngine.Instance.Updated -= OnUpdated;
    }

    private void OnSession(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SessionState.SelectedProcess) or null) SyncSelection();
    }

    private void OnUpdated()
    {
        ApplyFilter();
        if (TreeToggle.IsChecked == true)
            ProcessTree.ItemsSource = ProcessCatalogService.Instance.BuildTree(Filtered());
    }

    private void ApplyFilter()
    {
        var items = Filtered().ToList();
        _suppress = true;
        CollectionSync.MergeByPid(_visible, items, p => p.Pid);
        Subtitle.Text = $"{_visible.Count} visibles de {MonitorEngine.Instance.Processes.Count}";
        _suppress = false;
        SyncSelection();
    }

    private void SyncSelection()
    {
        var pid = SessionState.Current.SelectedProcess?.Pid;
        if (pid is null)
        {
            _suppress = true;
            ProcessList.SelectedItem = null;
            _suppress = false;
            return;
        }

        var match = _visible.FirstOrDefault(p => p.Pid == pid);
        if (match == null || ReferenceEquals(ProcessList.SelectedItem, match)) return;
        _suppress = true;
        ProcessList.SelectedItem = match;
        ProcessList.ScrollIntoView(match);
        _suppress = false;
    }

    private IEnumerable<ProcessRow> Filtered()
    {
        IEnumerable<ProcessRow> rows = MonitorEngine.Instance.Processes;
        var q = SearchBox.Text?.Trim() ?? "";
        if (!string.IsNullOrEmpty(q))
        {
            rows = rows.Where(p =>
                p.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                p.Pid.ToString().Contains(q) ||
                (p.Path?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (p.User?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (p.WindowTitle?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false));
        }
        if (ManagedFilter.IsChecked == true) rows = rows.Where(p => p.IsManaged);
        if (HangFilter.IsChecked == true) rows = rows.Where(p => !p.Responding);
        if (LeakFilter.IsChecked == true) rows = rows.Where(p => p.Leak?.IsLeak == true);
        return rows.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase);
    }

    private void Search_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args) => ApplyFilter();
    private void Filter_Click(object sender, RoutedEventArgs e) => ApplyFilter();
    private void Refresh_Click(object sender, RoutedEventArgs e) => OnUpdated();

    private void TreeToggle_Click(object sender, RoutedEventArgs e)
    {
        var tree = TreeToggle.IsChecked == true;
        ProcessTree.Visibility = tree ? Visibility.Visible : Visibility.Collapsed;
        ProcessList.Visibility = tree ? Visibility.Collapsed : Visibility.Visible;
        OnUpdated();
    }

    private void ProcessList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppress) return;
        if (ProcessList.SelectedItem is ProcessRow row)
            SessionState.Current.SelectedProcess = row;
    }

    private void Inspect_Click(object sender, RoutedEventArgs e) => App.Instance.Window?.GoTo("inspect");

    private void ProcessTree_ItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs args)
    {
        if (args.InvokedItem is ProcessNode node)
        {
            var row = MonitorEngine.Instance.Processes.FirstOrDefault(p => p.Pid == node.Pid);
            if (row != null) SessionState.Current.SelectedProcess = row;
        }
    }
}
