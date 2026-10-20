using System.Collections.ObjectModel;
using Consola.Helpers;
using Consola.Models;
using Consola.Services;
using Microsoft.UI.Xaml.Controls;

namespace Consola.Controls;

public sealed partial class ProcessPicker : UserControl
{
    private bool _suppress;
    private readonly ObservableCollection<ProcessRow> _items = [];

    public event EventHandler<ProcessRow>? ProcessChosen;

    public ProcessPicker()
    {
        InitializeComponent();
        Combo.ItemsSource = _items;
        Combo.DropDownOpened += (_, _) =>
        {
            MonitorEngine.Instance.FreezeUiLists = true;
            Fill();
        };
        Combo.DropDownClosed += (_, _) =>
        {
            MonitorEngine.Instance.FreezeUiLists = false;
            SyncSelection();
        };
        Loaded += (_, _) =>
        {
            MonitorEngine.Instance.Updated += OnUpdated;
            SessionState.Current.PropertyChanged += OnSession;
            UpdateHeader();
            SyncSelection();
        };
        Unloaded += (_, _) =>
        {
            MonitorEngine.Instance.Updated -= OnUpdated;
            SessionState.Current.PropertyChanged -= OnSession;
            MonitorEngine.Instance.FreezeUiLists = false;
        };
    }

    private void OnSession(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(SessionState.SelectedProcess) or null)) return;
        if (Combo.IsDropDownOpen) Fill();
        else SyncSelection();
    }

    private void OnUpdated()
    {
        UpdateHeader();
        if (Combo.IsDropDownOpen) return;
        SyncSelection();
    }

    private void Search_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.ProgrammaticChange) return;
        Fill();
    }

    private void Combo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppress) return;
        if (Combo.SelectedItem is not ProcessRow row) return;
        SessionState.Current.SelectedProcess = row;
        ProcessChosen?.Invoke(this, row);
    }

    private void Fill()
    {
        var q = SearchBox.Text?.Trim() ?? "";
        var desired = MonitorEngine.Instance.Processes
            .Where(p => Matches(p, q))
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p.Pid)
            .ToList();

        _suppress = true;
        try
        {
            CollectionSync.MergeByPid(_items, desired, p => p.Pid);
            UpdateHeader();
            ApplySelection();
        }
        finally
        {
            _suppress = false;
        }
    }

    private void UpdateHeader()
    {
        var count = MonitorEngine.Instance.Processes.Count;
        var countHeader = count > 0 ? $"Programas activos ({count})" : "Programas activos";
        if (!Equals(Combo.Header, countHeader))
            Combo.Header = countHeader;
    }

    private void SyncSelection()
    {
        _suppress = true;
        try { ApplySelection(); }
        finally { _suppress = false; }
    }

    private void ApplySelection()
    {
        var selected = SessionState.Current.SelectedProcess;
        if (selected == null)
        {
            Combo.SelectedItem = null;
            if (!Combo.IsDropDownOpen && _items.Count > 0)
                _items.Clear();
            return;
        }

        var match = _items.FirstOrDefault(p => p.Pid == selected.Pid);
        if (match == null)
        {
            if (!Combo.IsDropDownOpen)
            {
                _items.Clear();
                _items.Add(selected);
                match = selected;
            }
            else return;
        }

        if (!ReferenceEquals(Combo.SelectedItem, match))
            Combo.SelectedItem = match;
    }

    private static bool Matches(ProcessRow p, string q)
    {
        if (string.IsNullOrEmpty(q)) return true;
        return p.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
               || p.Pid.ToString().Contains(q)
               || (p.WindowTitle?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)
               || (p.Path?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false);
    }
}
