using System.ComponentModel;
using Forms = System.Windows.Forms;
using System.Windows;
using System.Windows.Threading;

namespace HeartRateAntPlus;

public partial class MainWindow : Window
{
    private readonly HeartRateStatistics _sessionStats = new();
    private readonly Queue<(DateTime Timestamp, int Bpm)> _recentReadings = new();
    private readonly HeartRateHistory _history = new();
    private HeartRateGraphWindow? _graphWindow;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private IHeartRateSource? _source;
    private readonly SettingsStore _settings = new();
    private readonly OscSender _osc;
    private CancellationTokenSource? _autoScanCancellation;
    private Task? _autoScanTask;
    private bool _manualConnectionInProgress;
    private bool _isClosing;
    private bool _exitRequested;
    private readonly bool _startupLaunch;
    private readonly Forms.NotifyIcon _trayIcon;

    public MainWindow()
    {
        InitializeComponent(); _timer.Tick += (_, _) => Refresh(); _settings.Load(); _settings.ApplyStartupRegistration(); _osc = new OscSender(_settings); _startupLaunch = Environment.GetCommandLineArgs().Any(x => string.Equals(x, "--startup", StringComparison.OrdinalIgnoreCase)); _trayIcon = CreateTrayIcon(); StateChanged += (_, _) => HandleWindowStateChanged(); UpdateOscUi();
        FooterText.Text = "心拍センサを装着して電源を入れてから接続してください。";
        Loaded += (_, _) => { StartAutoScan(); if (_startupLaunch) HideToTray(); };
    }

    private Forms.NotifyIcon CreateTrayIcon()
    {
        var menu = new Forms.ContextMenuStrip();
        var show = new Forms.ToolStripMenuItem("ウィンドウを表示"); show.Click += (_, _) => ShowFromTray();
        var exit = new Forms.ToolStripMenuItem("終了"); exit.Click += (_, _) => ExitFromTray();
        menu.Items.Add(show); menu.Items.Add(new Forms.ToolStripSeparator()); menu.Items.Add(exit);
        var icon = new Forms.NotifyIcon { Icon = LoadTrayIcon(), Visible = true, Text = "HeartRate OSC Bridge", ContextMenuStrip = menu };
        icon.DoubleClick += (_, _) => ShowFromTray();
        return icon;
    }

    private static System.Drawing.Icon LoadTrayIcon()
    {
        var resource = System.Windows.Application.GetResourceStream(new Uri("/HeartRateOscBridge;component/Assets/HeartRateOscBridge.png", UriKind.Relative));
        if (resource is null) return System.Drawing.SystemIcons.Application;
        using var stream = resource.Stream;
        using var bitmap = new System.Drawing.Bitmap(stream);
        var handle = bitmap.GetHicon();
        using var source = System.Drawing.Icon.FromHandle(handle);
        return (System.Drawing.Icon)source.Clone();
    }

    private void HandleWindowStateChanged()
    {
        if (WindowState == WindowState.Minimized && _settings.MinimizeToTray) HideToTray();
    }

    private void HideToTray() { ShowInTaskbar = false; Hide(); }
    private void ShowFromTray() { ShowInTaskbar = true; Show(); WindowState = WindowState.Normal; Activate(); }
    private void ExitFromTray() { _exitRequested = true; Close(); }

    private async void ConnectClick(object sender, RoutedEventArgs e)
    {
        if (_source is not null) { Disconnect(); return; }
        _manualConnectionInProgress = true; await StopAutoScanAsync(); ConnectButton.IsEnabled = false;
        try
        {
            if (_settings.Simulation)
            {
                await ConnectSourceAsync(new SimulatedHeartRateSource(), null, CancellationToken.None);
            }
            else
            {
                var picker = new DeviceSelectionWindow { Owner = this };
                if (picker.ShowDialog() == true && picker.SelectedDevice is not null) await ConnectSourceAsync(new BluetoothLeHeartRateSource(picker.SelectedDevice), picker.SelectedDevice, CancellationToken.None);
            }
        }
        catch (Exception ex) { Disconnect(false); System.Windows.MessageBox.Show(ex.Message, "接続エラー", MessageBoxButton.OK, MessageBoxImage.Error); }
        finally { _manualConnectionInProgress = false; ConnectButton.IsEnabled = true; if (_source is null) StartAutoScan(); }
    }

    private async Task<bool> ConnectSourceAsync(IHeartRateSource source, BleDeviceInfo? selected, CancellationToken cancellationToken)
    {
        _source = source;
        _source.HeartRateChanged += (_, bpm) => Dispatcher.Invoke(() => UpdateHeartRate(bpm));
        _source.BatteryLevelChanged += (_, level) => Dispatcher.Invoke(() => UpdateBatteryLevel(level));
        _source.ConnectionLost += SourceConnectionLost;
        try
        {
            BatteryText.Text = "  電池 --";
            StatusText.Text = "接続中…"; StatusText.Foreground = System.Windows.Media.Brushes.Gold;
            await source.StartAsync(cancellationToken); _timer.Start(); ConnectButton.Content = "切断"; StatusText.Text = "接続中"; StatusText.Foreground = System.Windows.Media.Brushes.LightGreen; _osc.SendConnectionState(true);
            if (selected is not null) { _settings.LastDeviceAddress = selected.Address; _settings.LastDeviceName = selected.Name; _settings.Save(); }
            return true;
        }
        catch
        {
            source.ConnectionLost -= SourceConnectionLost; source.Dispose(); if (ReferenceEquals(_source, source)) _source = null; throw;
        }
    }

    private void SourceConnectionLost(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (sender is not IHeartRateSource source || !ReferenceEquals(_source, source) || _isClosing) return;
            _source = null; source.ConnectionLost -= SourceConnectionLost; source.Dispose(); _timer.Stop(); _osc.SendConnectionState(false); ConnectButton.Content = "接続"; BpmText.Text = "--"; SensorText.Text = "心拍センサ: 未接続"; BatteryText.Text = "  電池 --"; StatusText.Text = "切断。再スキャン中…"; StatusText.Foreground = System.Windows.Media.Brushes.Gold; UpdateStatsDisplay(); StartAutoScan();
        });
    }

    private void StartAutoScan()
    {
        if (_settings.Simulation || _isClosing || _manualConnectionInProgress || _autoScanTask is { IsCompleted: false }) return;
        _autoScanCancellation = new CancellationTokenSource(); _autoScanTask = AutoScanLoopAsync(_autoScanCancellation.Token);
    }

    private async Task StopAutoScanAsync()
    {
        var cancellation = _autoScanCancellation;
        var task = _autoScanTask;
        _autoScanCancellation = null;
        _autoScanTask = null;
        cancellation?.Cancel();
        if (task is not null)
        {
            try { await task; }
            catch (OperationCanceledException) { }
            catch { }
        }
        cancellation?.Dispose();
    }

    private void StopAutoScan() { _autoScanCancellation?.Cancel(); _autoScanCancellation = null; _autoScanTask = null; }

    private async Task AutoScanLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && !_isClosing)
        {
            try
            {
                if (_source is null && !_manualConnectionInProgress)
                {
                    StatusText.Text = _settings.LastDeviceAddress.HasValue ? "自動スキャン中…" : "スキャン中…"; StatusText.Foreground = System.Windows.Media.Brushes.Gold;
                    var devices = await BleScanner.ScanAsync(TimeSpan.FromSeconds(5), cancellationToken: cancellationToken);
                    var remembered = FindRememberedDevice(devices);
                    if (remembered is not null && _source is null && !_manualConnectionInProgress)
                    {
                        try { await ConnectSourceAsync(new BluetoothLeHeartRateSource(remembered), remembered, cancellationToken); }
                        catch { if (_source is null) { ConnectButton.Content = "接続"; StatusText.Text = "自動スキャン中…"; } }
                    }
                }
                await Task.Delay(250, cancellationToken);
            }
            catch (OperationCanceledException) { break; }
            catch { await Task.Delay(1000, cancellationToken); }
        }
    }

    private BleDeviceInfo? FindRememberedDevice(IReadOnlyList<BleDeviceInfo> devices)
    {
        if (!_settings.LastDeviceAddress.HasValue) return null;
        return devices.FirstOrDefault(x => x.Address == _settings.LastDeviceAddress.Value) ?? devices.FirstOrDefault(x => !string.IsNullOrWhiteSpace(_settings.LastDeviceName) && x.Name.Equals(_settings.LastDeviceName, StringComparison.OrdinalIgnoreCase));
    }

    private void Disconnect(bool restartScan = true)
    {
        _timer.Stop(); if (_source is not null) { _source.ConnectionLost -= SourceConnectionLost; _source.Dispose(); _osc.SendConnectionState(false); } _source = null; ConnectButton.Content = "接続"; StatusText.Text = "未接続"; StatusText.Foreground = System.Windows.Media.Brushes.LightGray; BpmText.Text = "--"; SensorText.Text = "心拍センサ: 未検出"; BatteryText.Text = "  電池 --"; UpdateStatsDisplay();
        if (restartScan) StartAutoScan();
    }

    private void Refresh() { _source?.Poll(); UpdateStatsDisplay(); }
    private int? _lastBpm;
    private void UpdateBatteryLevel(int? level) { BatteryText.Text = level is >= 0 and <= 100 ? $"  電池 {level}%" : "  電池 --"; }
    private void UpdateHeartRate(int bpm) { var now = DateTime.Now; BpmText.Text = bpm.ToString(); SensorText.Text = $"心拍センサ: {_source?.SensorId ?? "Bluetooth LE"}"; _lastBpm = bpm; _sessionStats.Add(bpm); _recentReadings.Enqueue((now, bpm)); TrimRecentReadings(now); _history.Add(bpm); UpdateStatsDisplay(); _osc.SendHeartRate(bpm, CurrentAverage()); }
    private void ResetStatsClick(object sender, RoutedEventArgs e) => ResetStats();
    private void ResetStats() { _sessionStats.Clear(); _recentReadings.Clear(); _lastBpm = null; _history.Clear(); SessionStatsText.Text = "最大 --  最小 --  平均 --"; RecentStatsText.Text = "最大 --  最小 --  平均 --"; }
    private void GraphClick(object sender, RoutedEventArgs e) { if (_graphWindow is { IsVisible: true }) { _graphWindow.Activate(); return; } _graphWindow = new HeartRateGraphWindow(_history) { Owner = this }; _graphWindow.Show(); }
    private void TrimRecentReadings(DateTime now) { while (_recentReadings.Count > 0 && _recentReadings.Peek().Timestamp < now.AddMinutes(-5)) _recentReadings.Dequeue(); }
    private void UpdateStatsDisplay() { TrimRecentReadings(DateTime.Now); SetStatsText(SessionStatsText, _sessionStats); var recent = new HeartRateStatistics(); foreach (var reading in _recentReadings) recent.Add(reading.Bpm); SetStatsText(RecentStatsText, recent); }
    private static void SetStatsText(System.Windows.Controls.TextBlock target, HeartRateStatistics stats) { target.Text = !stats.HasValue ? "最大 --  最小 --  平均 --" : $"最大 {stats.Maximum}  最小 {stats.Minimum}  平均 {stats.Average:0.0}"; }
    private int CurrentAverage() => _sessionStats.HasValue ? (int)Math.Round(_sessionStats.Average) : 0;
    private void SettingsClick(object sender, RoutedEventArgs e) { var dialog = new SettingsWindow(_settings) { Owner = this }; if (dialog.ShowDialog() == true) { _settings.Save(); _settings.ApplyStartupRegistration(); _osc.ApplySettings(_settings); UpdateOscUi(); if (_source is not null) { _osc.SendConnectionState(true); if (_lastBpm.HasValue) _osc.SendHeartRate(_lastBpm.Value, CurrentAverage()); } if (_settings.Simulation) StopAutoScan(); else StartAutoScan(); } }
    private void OscToggleClick(object sender, RoutedEventArgs e) { _settings.OscEnabled = !_settings.OscEnabled; _settings.Save(); _osc.ApplySettings(_settings); if (_settings.OscEnabled && _source is not null) { _osc.SendConnectionState(true); if (_lastBpm.HasValue) _osc.SendHeartRate(_lastBpm.Value, CurrentAverage()); } UpdateOscUi(); }
    private void UpdateOscUi() { OscStatusText.Text = _settings.OscEnabled ? "OSC: ON" : "OSC: OFF"; OscStatusText.Foreground = _settings.OscEnabled ? System.Windows.Media.Brushes.LightGreen : System.Windows.Media.Brushes.LightGray; OscToggleButton.Content = _settings.OscEnabled ? "OSC OFF" : "OSC ON"; }
    protected override void OnClosing(CancelEventArgs e) { if (!_exitRequested && _settings.MinimizeToTray) { e.Cancel = true; HideToTray(); return; } base.OnClosing(e); }
    protected override void OnClosed(EventArgs e) { _isClosing = true; StopAutoScan(); _graphWindow?.Close(); Disconnect(false); _osc.Dispose(); _trayIcon.Visible = false; _trayIcon.Dispose(); base.OnClosed(e); }
}
