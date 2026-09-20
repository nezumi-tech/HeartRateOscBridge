using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.IO;
using Microsoft.Win32;

namespace HeartRateAntPlus;

public sealed class SettingsStore
{
    public bool Simulation { get; set; }
    public bool StartWithWindows { get; set; }
    public bool MinimizeToTray { get; set; } = true;
    public ulong? LastDeviceAddress { get; set; }
    public string LastDeviceName { get; set; } = "";
    public bool OscEnabled { get; set; }
    public string OscHost { get; set; } = "127.0.0.1";
    public int OscPort { get; set; } = 9000;
    public bool ChatboxEnabled { get; set; }
    public double NormalisedDenominator { get; set; } = 240;
    public bool VrcoscValue { get; set; } = true;
    public bool VrcoscNormalised { get; set; } = true;
    public bool VrcoscConnected { get; set; } = true;
    public bool VrcoscEnabled { get; set; } = true;
    public bool VrcoscAverage { get; set; } = true;
    public bool VrcoscBeat { get; set; }
    public bool VrcoscUnits { get; set; } = true;
    public bool VrcoscTens { get; set; } = true;
    public bool VrcoscHundreds { get; set; } = true;
    public bool LegacyHr { get; set; } = true;
    public bool LegacyConnected { get; set; } = true;
    public bool LegacyActive { get; set; } = true;
    public bool LegacyBeat { get; set; }
    public bool LegacyBeatToggle { get; set; }
    private static string PathName => Path.Combine(AppContext.BaseDirectory, "settings.json");
    public void Load()
    {
        if (!File.Exists(PathName)) return;
        var x = JsonSerializer.Deserialize<SettingsStore>(File.ReadAllText(PathName));
        if (x is null) return;
        Simulation = x.Simulation; StartWithWindows = x.StartWithWindows; MinimizeToTray = x.MinimizeToTray; LastDeviceAddress = x.LastDeviceAddress; LastDeviceName = x.LastDeviceName; OscEnabled = x.OscEnabled; OscHost = x.OscHost; OscPort = x.OscPort; ChatboxEnabled = x.ChatboxEnabled; NormalisedDenominator = x.NormalisedDenominator;
        VrcoscValue = x.VrcoscValue; VrcoscNormalised = x.VrcoscNormalised; VrcoscConnected = x.VrcoscConnected; VrcoscEnabled = x.VrcoscEnabled; VrcoscAverage = x.VrcoscAverage; VrcoscBeat = x.VrcoscBeat; VrcoscUnits = x.VrcoscUnits; VrcoscTens = x.VrcoscTens; VrcoscHundreds = x.VrcoscHundreds;
        LegacyHr = x.LegacyHr; LegacyConnected = x.LegacyConnected; LegacyActive = x.LegacyActive; LegacyBeat = x.LegacyBeat; LegacyBeatToggle = x.LegacyBeatToggle;
        if (NormalisedDenominator <= 0) NormalisedDenominator = 240;
    }
    public void Save() { Directory.CreateDirectory(Path.GetDirectoryName(PathName)!); File.WriteAllText(PathName, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true })); }
    public void ApplyStartupRegistration()
    {
        const string runPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string valueName = "HeartRateOscBridge";
        const string oldValueName = "BluetoothHeartRateMonitor";
        using var key = Registry.CurrentUser.OpenSubKey(runPath, writable: true) ?? Registry.CurrentUser.CreateSubKey(runPath);
        if (key is null) return;
        if (StartWithWindows)
        {
            var executable = Environment.ProcessPath ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
            if (!string.IsNullOrWhiteSpace(executable)) key.SetValue(valueName, $"\"{executable}\" --startup");
        }
        else key.DeleteValue(valueName, throwOnMissingValue: false);
        key.DeleteValue(oldValueName, throwOnMissingValue: false);
    }
}

public sealed class SettingsWindow : Window
{
    private readonly SettingsStore _s;
    private readonly System.Windows.Controls.CheckBox _sim = Check("シミュレーションモード（実機なし）");
    private readonly System.Windows.Controls.CheckBox _startup = Check("Windowsのスタートアップに登録する");
    private readonly System.Windows.Controls.CheckBox _tray = Check("最小化・閉じるときにタスクトレイへ格納する");
    private readonly System.Windows.Controls.CheckBox _osc = Check("OSC送信を有効にする");
    private readonly System.Windows.Controls.TextBox _host = new();
    private readonly System.Windows.Controls.TextBox _port = new();
    private readonly System.Windows.Controls.TextBox _denominator = new();
    private readonly Dictionary<string, System.Windows.Controls.CheckBox> _checks = new();
    public SettingsWindow(SettingsStore s)
    {
        _s = s; s.Load(); Title = "設定"; Width = 560; Height = 700; MinWidth = 520; MinHeight = 620; Background = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#101216")!; Foreground = System.Windows.Media.Brushes.White; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        StyleTextBox(_host); StyleTextBox(_port); StyleTextBox(_denominator);
        var root = new System.Windows.Controls.StackPanel { Margin = new Thickness(22) }; _sim.IsChecked = s.Simulation; _startup.IsChecked = s.StartWithWindows; _tray.IsChecked = s.MinimizeToTray; root.Children.Add(_sim); root.Children.Add(_startup); root.Children.Add(_tray); root.Children.Add(new System.Windows.Controls.Separator { Margin = new Thickness(0, 8, 0, 8), Background = System.Windows.Media.Brushes.Gray });
        root.Children.Add(new System.Windows.Controls.TextBlock { Text = "OSC送信設定", FontWeight = FontWeights.SemiBold, Foreground = System.Windows.Media.Brushes.White }); _osc.IsChecked = s.OscEnabled; root.Children.Add(_osc);
        root.Children.Add(Label("送信先IPアドレス / ホスト名")); _host.Text = s.OscHost; root.Children.Add(_host);
        root.Children.Add(Label("送信先ポート")); _port.Text = s.OscPort.ToString(CultureInfo.InvariantCulture); root.Children.Add(_port);
        AddCheck(root, "ChatboxEnabled", "/chatbox/input（♡ BPMをチャットボックスへ送信）", s.ChatboxEnabled);
        root.Children.Add(Label("Normalisedの分母")); _denominator.Text = s.NormalisedDenominator.ToString(CultureInfo.InvariantCulture); root.Children.Add(_denominator);
        root.Children.Add(new System.Windows.Controls.TextBlock { Text = "VRCOSC互換パラメータ", Margin = new Thickness(0, 14, 0, 4), FontWeight = FontWeights.SemiBold, Foreground = System.Windows.Media.Brushes.White });
        AddCheck(root, "VrcoscValue", "/avatar/parameters/VRCOSC/Heartrate/Value（Int）", s.VrcoscValue); AddCheck(root, "VrcoscNormalised", "/avatar/parameters/VRCOSC/Heartrate/Normalised（Float）", s.VrcoscNormalised); AddCheck(root, "VrcoscConnected", "/avatar/parameters/VRCOSC/Heartrate/Connected（Bool）", s.VrcoscConnected); AddCheck(root, "VrcoscEnabled", "/avatar/parameters/VRCOSC/Heartrate/Enabled（Bool）", s.VrcoscEnabled); AddCheck(root, "VrcoscAverage", "/avatar/parameters/VRCOSC/Heartrate/Average（Int）", s.VrcoscAverage); AddCheck(root, "VrcoscBeat", "/avatar/parameters/VRCOSC/Heartrate/Beat（Bool）", s.VrcoscBeat); AddCheck(root, "VrcoscUnits", "/avatar/parameters/VRCOSC/Heartrate/Units（Float）", s.VrcoscUnits); AddCheck(root, "VrcoscTens", "/avatar/parameters/VRCOSC/Heartrate/Tens（Float）", s.VrcoscTens); AddCheck(root, "VrcoscHundreds", "/avatar/parameters/VRCOSC/Heartrate/Hundreds（Float）", s.VrcoscHundreds);
        root.Children.Add(new System.Windows.Controls.TextBlock { Text = "後方互換パラメータ", Margin = new Thickness(0, 14, 0, 4), FontWeight = FontWeights.SemiBold, Foreground = System.Windows.Media.Brushes.White });
        AddCheck(root, "LegacyHr", "/avatar/parameters/HR（Int）", s.LegacyHr); AddCheck(root, "LegacyConnected", "/avatar/parameters/isHRConnected（Bool）", s.LegacyConnected); AddCheck(root, "LegacyActive", "/avatar/parameters/isHRActive（Bool）", s.LegacyActive); AddCheck(root, "LegacyBeat", "/avatar/parameters/isHRBeat（Bool）", s.LegacyBeat); AddCheck(root, "LegacyBeatToggle", "/avatar/parameters/HeartBeatToggle（Bool）", s.LegacyBeatToggle);
        var save = new System.Windows.Controls.Button { Content = "保存", Width = 90, Height = 32, Margin = new Thickness(0, 18, 0, 0), HorizontalAlignment = System.Windows.HorizontalAlignment.Right }; save.Click += SaveClick; root.Children.Add(save); Content = new System.Windows.Controls.ScrollViewer { Background = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#101216")!, VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto, Content = root };
    }
    private void AddCheck(System.Windows.Controls.Panel panel, string key, string text, bool value) { var check = Check(text); check.IsChecked = value; _checks[key] = check; panel.Children.Add(check); }
    private void SaveClick(object? sender, RoutedEventArgs e)
    {
        if (!int.TryParse(_port.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var port) || port is < 1 or > 65535) { System.Windows.MessageBox.Show("ポートは1〜65535で入力してください。", "設定エラー"); return; }
        if (!double.TryParse(_denominator.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var denominator) || denominator <= 0) { System.Windows.MessageBox.Show("Normalisedの分母は0より大きい数値で入力してください。", "設定エラー"); return; }
        _s.Simulation = _sim.IsChecked == true; _s.StartWithWindows = _startup.IsChecked == true; _s.MinimizeToTray = _tray.IsChecked == true; _s.OscEnabled = _osc.IsChecked == true; _s.OscHost = string.IsNullOrWhiteSpace(_host.Text) ? "127.0.0.1" : _host.Text.Trim(); _s.OscPort = port; _s.NormalisedDenominator = denominator;
        foreach (var item in _checks) typeof(SettingsStore).GetProperty(item.Key)!.SetValue(_s, item.Value.IsChecked == true); DialogResult = true;
    }
    private static System.Windows.Controls.CheckBox Check(string text) => new() { Content = new System.Windows.Controls.TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = System.Windows.Media.Brushes.White }, Width = 490, HorizontalContentAlignment = System.Windows.HorizontalAlignment.Left, Margin = new Thickness(0, 2, 0, 2), Foreground = System.Windows.Media.Brushes.White };
    private static System.Windows.Controls.TextBlock Label(string text) => new() { Text = text, Foreground = System.Windows.Media.Brushes.LightGray, Margin = new Thickness(0, 8, 0, 2) };
    private static void StyleTextBox(System.Windows.Controls.TextBox box) { box.Background = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#20242C")!; box.Foreground = System.Windows.Media.Brushes.White; box.BorderBrush = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#667080")!; box.CaretBrush = System.Windows.Media.Brushes.White; }
}
