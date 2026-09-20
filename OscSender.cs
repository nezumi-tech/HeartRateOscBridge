using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text;

namespace HeartRateAntPlus;

public sealed class OscSender : IDisposable
{
    private delegate void Writer(byte[] buffer, int offset);
    private readonly UdpClient _udp = new();
    private SettingsStore _settings;
    public OscSender(SettingsStore settings) => _settings = settings;
    public void ApplySettings(SettingsStore settings) => _settings = settings;
    public void SendHeartRate(int bpm, int average)
    {
        if (!_settings.OscEnabled) return;
        if (_settings.VrcoscValue) SendInt("/avatar/parameters/VRCOSC/Heartrate/Value", bpm);
        if (_settings.VrcoscNormalised) SendFloat("/avatar/parameters/VRCOSC/Heartrate/Normalised", Math.Clamp(bpm / (float)Math.Max(1, _settings.NormalisedDenominator), 0f, 1f));
        if (_settings.VrcoscAverage) SendInt("/avatar/parameters/VRCOSC/Heartrate/Average", average);
        if (_settings.VrcoscBeat) SendBool("/avatar/parameters/VRCOSC/Heartrate/Beat", !_beatToggle);
        var digits = Math.Clamp(bpm, 0, 999).ToString("D3");
        if (_settings.VrcoscHundreds) SendFloat("/avatar/parameters/VRCOSC/Heartrate/Hundreds", (digits[0] - '0') / 10f);
        if (_settings.VrcoscTens) SendFloat("/avatar/parameters/VRCOSC/Heartrate/Tens", (digits[1] - '0') / 10f);
        if (_settings.VrcoscUnits) SendFloat("/avatar/parameters/VRCOSC/Heartrate/Units", (digits[2] - '0') / 10f);
        if (_settings.LegacyHr) SendInt("/avatar/parameters/HR", bpm);
        if (_settings.LegacyActive) SendBool("/avatar/parameters/isHRActive", true);
        if (_settings.LegacyBeatToggle) SendBool("/avatar/parameters/HeartBeatToggle", !_beatToggle);
        _beatToggle = !_beatToggle;
        if (_settings.ChatboxEnabled) SendChatbox(bpm);
    }
    private bool _beatToggle;
    public void SendConnectionState(bool connected)
    {
        if (!_settings.OscEnabled) return;
        if (_settings.VrcoscConnected) SendBool("/avatar/parameters/VRCOSC/Heartrate/Connected", connected);
        if (_settings.VrcoscEnabled) SendBool("/avatar/parameters/VRCOSC/Heartrate/Enabled", connected);
        if (_settings.LegacyConnected) SendBool("/avatar/parameters/isHRConnected", connected);
        if (_settings.LegacyActive) SendBool("/avatar/parameters/isHRActive", connected);
        if (_settings.LegacyBeat) SendBool("/avatar/parameters/isHRBeat", false);
    }
    private void SendInt(string address, int value) => Send(address, ",i", (data, offset) => BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(offset, 4), value));
    private void SendFloat(string address, float value) => Send(address, ",f", (data, offset) => BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(offset, 4), BitConverter.SingleToInt32Bits(value)));
    private void SendBool(string address, bool value) => Send(address, value ? ",T" : ",F", null);
    private void SendChatbox(int bpm)
    {
        var text = $"♡ {bpm} bpm";
        SendChatboxPacket("/chatbox/input", text, true, false);
    }
    private void SendChatboxPacket(string address, string text, bool immediate, bool notification)
    {
        try
        {
            var addressBytes = Pad(Encoding.UTF8.GetBytes(address));
            var tags = $",s{(immediate ? "T" : "F")}{(notification ? "T" : "F")}";
            var tagBytes = Pad(Encoding.ASCII.GetBytes(tags));
            var textBytes = Pad(Encoding.UTF8.GetBytes(text));
            var data = new byte[addressBytes.Length + tagBytes.Length + textBytes.Length];
            addressBytes.CopyTo(data, 0);
            tagBytes.CopyTo(data, addressBytes.Length);
            textBytes.CopyTo(data, addressBytes.Length + tagBytes.Length);
            _udp.Send(data, data.Length, _settings.OscHost, _settings.OscPort);
        }
        catch { }
    }
    private void Send(string address, string tags, Writer? write)
    {
        try
        {
            var addressBytes = Pad(Encoding.UTF8.GetBytes(address)); var tagBytes = Pad(Encoding.ASCII.GetBytes(tags)); var data = new byte[addressBytes.Length + tagBytes.Length + (write is null ? 0 : 4)];
            addressBytes.CopyTo(data, 0); tagBytes.CopyTo(data, addressBytes.Length); if (write is not null) write(data, addressBytes.Length + tagBytes.Length);
            _udp.Send(data, data.Length, _settings.OscHost, _settings.OscPort);
        }
        catch { }
    }
    private static byte[] Pad(byte[] bytes) { var length = ((bytes.Length + 4) / 4) * 4; var result = new byte[length]; bytes.CopyTo(result, 0); return result; }
    public void Dispose() => _udp.Dispose();
}
