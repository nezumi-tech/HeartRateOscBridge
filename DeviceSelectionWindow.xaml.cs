using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;

namespace HeartRateAntPlus;

public partial class DeviceSelectionWindow : Window
{
    private readonly ObservableCollection<BleDeviceInfo> _devices = new();
    private CancellationTokenSource? _scanCancellation;
    public BleDeviceInfo? SelectedDevice { get; private set; }

    public DeviceSelectionWindow()
    {
        InitializeComponent(); DevicesList.ItemsSource = _devices; Loaded += async (_, _) => await ScanAsync(); Closed += (_, _) => _scanCancellation?.Cancel();
    }

    private async Task ScanAsync()
    {
        _scanCancellation?.Cancel(); _scanCancellation = new CancellationTokenSource(); _devices.Clear(); SelectButton.IsEnabled = false; RescanButton.IsEnabled = false; StatusText.Text = "周囲のBLEデバイスをスキャン中（8秒）…";
        try
        {
            await BleScanner.ScanAsync(TimeSpan.FromSeconds(8), info => Dispatcher.Invoke(() => _devices.Add(info)), _scanCancellation.Token);
            StatusText.Text = _devices.Count == 0 ? "デバイスが見つかりませんでした。再スキャンしてください。" : $"{_devices.Count}台見つかりました。心拍センサを選択してください。";
        }
        catch (OperationCanceledException) { }
        finally { RescanButton.IsEnabled = true; }
    }

    private async void RescanClick(object sender, RoutedEventArgs e) => await ScanAsync();
    private void SelectClick(object sender, RoutedEventArgs e) => AcceptSelection();
    private void DeviceDoubleClick(object sender, MouseButtonEventArgs e) => AcceptSelection();
    private void DeviceSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        SelectButton.IsEnabled = DevicesList.SelectedItem is BleDeviceInfo;
    }
    private void AcceptSelection()
    {
        if (DevicesList.SelectedItem is not BleDeviceInfo device) return;
        SelectedDevice = device; DialogResult = true;
    }
}
