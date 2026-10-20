using Consola.Helpers;
using Consola.Models;
using Consola.Services;
using Microsoft.UI;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;

namespace Consola.Pages;

public sealed partial class MemoryPage : Page
{
    private int _samplePid = -1;
    private int _sampleCount = -1;

    public MemoryPage()
    {
        InitializeComponent();
        SparkCanvas.SizeChanged += (_, _) => DrawSpark();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        SessionState.Current.PropertyChanged += OnSession;
        MonitorEngine.Instance.Updated += Refresh;
        Refresh();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        SessionState.Current.PropertyChanged -= OnSession;
        MonitorEngine.Instance.Updated -= Refresh;
    }

    private void OnSession(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SessionState.SelectedProcess) or null) Refresh();
    }

    private void Refresh()
    {
        var row = SessionState.Current.SelectedProcess;
        TargetText.Text = row == null
            ? "Elige un programa en Programas activos. Aquí verás su serie temporal y el dictamen de fugas."
            : $"{row.Name} ({row.Pid}) · muestreo cada ~1,5 s";

        if (row == null)
        {
            _samplePid = -1;
            _sampleCount = -1;
            VerdictTitle.Text = "Sin proceso";
            VerdictDetail.Text = "Elige un programa en Programas activos para ver su serie temporal y el dictamen de fugas.";
            PrivateValue.Text = "—";
            HandleValue.Text = "—";
            GdiValue.Text = "—";
            SampleList.ItemsSource = null;
        }
        else
        {
            var live = MonitorEngine.Instance.Processes.FirstOrDefault(p => p.Pid == row.Pid) ?? row;
            var verdict = MemoryProfilerService.Instance.Analyze(live.Pid);
            VerdictTitle.Text = verdict.Summary;
            VerdictDetail.Text = verdict.Details;
            PrivateValue.Text = live.MemoryText;
            HandleValue.Text = live.HandleCount.ToString("N0");
            GdiValue.Text = $"{live.GdiCount} / {live.UserObjectCount}";
            var history = MemoryProfilerService.Instance.GetHistory(live.Pid);
            if (_samplePid != live.Pid || _sampleCount != history.Count)
            {
                _samplePid = live.Pid;
                _sampleCount = history.Count;
                SampleList.ItemsSource = history.AsEnumerable().Reverse().ToList();
            }
        }

        if (!MonitorEngine.Instance.FreezeUiLists)
        {
            IEnumerable<(ProcessRow process, LeakVerdict leak)> leaks = MonitorEngine.Instance.Leaks;
            if (row != null)
                leaks = leaks.Where(l => l.process.Pid == row.Pid);
            GlobalLeaks.ItemsSource = leaks.Select(l => new DashboardAlert
            {
                Title = $"{l.process.Name} ({l.process.Pid})  ·  {l.leak.Summary}",
                Message = l.leak.Details,
                Pid = l.process.Pid
            }).ToList();
        }

        DrawSpark();
    }

    private void DrawSpark()
    {
        SparkCanvas.Children.Clear();
        var row = SessionState.Current.SelectedProcess;
        if (row == null) return;
        var samples = MemoryProfilerService.Instance.GetHistory(row.Pid);
        if (samples.Count < 2) return;
        var w = SparkCanvas.ActualWidth;
        var h = SparkCanvas.ActualHeight;
        if (w < 8 || h < 8) return;

        var values = samples.Select(s => (double)s.PrivateBytes).ToArray();
        var min = values.Min();
        var max = values.Max();
        var range = Math.Max(1, max - min);
        var points = new PointCollection();
        for (var i = 0; i < values.Length; i++)
        {
            var x = i / (double)(values.Length - 1) * w;
            var y = h - ((values[i] - min) / range * (h - 8)) - 4;
            points.Add(new Windows.Foundation.Point(x, y));
        }

        SparkCanvas.Children.Add(new Polyline
        {
            Points = points,
            Stroke = new SolidColorBrush(Colors.LightSkyBlue),
            StrokeThickness = 2
        });
    }
}
