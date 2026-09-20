using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;

namespace HeartRateAntPlus;

public partial class HeartRateGraphWindow : Window
{
    private readonly HeartRateHistory _history;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(500) };

    public HeartRateGraphWindow(HeartRateHistory history)
    {
        InitializeComponent(); _history = history;
        _timer.Tick += (_, _) => Redraw();
        Loaded += (_, _) => { _timer.Start(); Redraw(); };
        Closed += (_, _) => _timer.Stop();
    }

    private void ChartSizeChanged(object sender, SizeChangedEventArgs e) => Redraw();

    private void Redraw()
    {
        if (!IsLoaded || ChartCanvas.ActualWidth < 100 || ChartCanvas.ActualHeight < 100) return;
        ChartCanvas.Children.Clear();
        var points = _history.Snapshot();
        const double left = 58, right = 18, top = 16, bottom = 42;
        var width = ChartCanvas.ActualWidth - left - right;
        var height = ChartCanvas.ActualHeight - top - bottom;
        var axisBrush = new SolidColorBrush(Color.FromRgb(120, 128, 142));
        var gridBrush = new SolidColorBrush(Color.FromRgb(55, 61, 72));

        AddLine(left, top, left, top + height, axisBrush, 1);
        AddLine(left, top + height, left + width, top + height, axisBrush, 1);
        if (points.Length == 0) { ChartStatus.Text = "心拍データを受信するとグラフが表示されます。"; return; }

        var first = points[0].Timestamp;
        var last = points[^1].Timestamp;
        var tickMinutes = ChooseTickMinutes(last - first);
        // 目盛りはデータ開始時刻ではなく、時計の0分を基準に揃える。
        // 例: 5分刻みなら xx:00 / xx:05 / xx:10 ... に配置する。
        var axisStart = FloorToInterval(first, tickMinutes);
        var axisEnd = CeilToInterval(last, tickMinutes);
        if (axisEnd <= axisStart) axisEnd = axisStart.AddMinutes(tickMinutes * 2);
        var axisSeconds = (axisEnd - axisStart).TotalSeconds;
        var values = points.Select(x => x.Bpm).ToArray();
        var valueMin = Math.Floor((values.Min() - 5) / 5.0) * 5;
        var valueMax = Math.Ceiling((values.Max() + 5) / 5.0) * 5;
        if (valueMax <= valueMin) valueMax = valueMin + 10;

        ChartStatus.Text = $"{first:HH:mm:ss} からのデータ　{points.Length}点";
        for (var i = 0; i <= 4; i++)
        {
            var y = top + height * i / 4;
            AddLine(left, y, left + width, y, gridBrush, 1);
            AddLabel($"{valueMax - (valueMax - valueMin) * i / 4:0}", 2, y - 8, 50, 16, TextAlignment.Right);
        }
        // 縦軸タイトルは最上段の目盛りラベルと重ならないよう、目盛り列の上側へ配置する。
        AddLabel("BPM", 0, 0, 50, 16, TextAlignment.Center);
        AddLabel("時刻", left + width / 2 - 25, top + height + 22, 50, 18, TextAlignment.Center);

        for (var tick = axisStart; tick <= axisEnd; tick = tick.AddMinutes(tickMinutes))
        {
            var x = left + (tick - axisStart).TotalSeconds / axisSeconds * width;
            AddLine(x, top + height, x, top + height + 5, axisBrush, 1);
            AddLabel(FormatTimeTick(tick), x - 30, top + height + 7, 60, 18, TextAlignment.Center);
        }

        var line = new Polyline { Stroke = new SolidColorBrush(Color.FromRgb(80, 190, 255)), StrokeThickness = 2, SnapsToDevicePixels = true };
        foreach (var point in points)
        {
            var x = left + (point.Timestamp - axisStart).TotalSeconds / axisSeconds * width;
            var y = top + (valueMax - point.Bpm) / (valueMax - valueMin) * height;
            line.Points.Add(new Point(Math.Clamp(x, left, left + width), Math.Clamp(y, top, top + height)));
        }
        ChartCanvas.Children.Add(line);

        // データ点を小さな丸で重ね、受信値の変化が少ない場合も線の位置を確認できるようにする。
        foreach (var point in points)
        {
            var x = left + (point.Timestamp - axisStart).TotalSeconds / axisSeconds * width;
            var y = top + (valueMax - point.Bpm) / (valueMax - valueMin) * height;
            var dot = new Ellipse { Width = 4, Height = 4, Fill = new SolidColorBrush(Color.FromRgb(150, 220, 255)) };
            Canvas.SetLeft(dot, Math.Clamp(x - 2, left - 2, left + width - 2)); Canvas.SetTop(dot, Math.Clamp(y - 2, top - 2, top + height - 2)); ChartCanvas.Children.Add(dot);
        }
    }

    private static int ChooseTickMinutes(TimeSpan span) => span.TotalMinutes <= 10 ? 1 : span.TotalMinutes <= 45 ? 5 : span.TotalHours <= 3 ? 15 : span.TotalHours <= 12 ? 30 : 60;
    private static DateTime FloorToInterval(DateTime value, int minutes)
    {
        var minute = value.Minute - value.Minute % minutes;
        return new DateTime(value.Year, value.Month, value.Day, value.Hour, minute, 0, value.Kind);
    }
    private static DateTime CeilToInterval(DateTime value, int minutes)
    {
        var floor = FloorToInterval(value, minutes);
        return floor == value ? floor : floor.AddMinutes(minutes);
    }
    private static string FormatTimeTick(DateTime value) => value.ToString("HH:mm");
    private void AddLine(double x1, double y1, double x2, double y2, System.Windows.Media.Brush brush, double thickness) => ChartCanvas.Children.Add(new Line { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Stroke = brush, StrokeThickness = thickness });
    private void AddLabel(string text, double left, double top, double width, double height, TextAlignment alignment) { var label = new TextBlock { Text = text, Foreground = new SolidColorBrush(Color.FromRgb(170, 176, 188)), FontSize = 11, Width = width, Height = height, TextAlignment = alignment }; Canvas.SetLeft(label, left); Canvas.SetTop(label, top); ChartCanvas.Children.Add(label); }
}
