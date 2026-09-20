namespace HeartRateAntPlus;

public sealed record HeartRatePoint(DateTime Timestamp, int Bpm);

public sealed class HeartRateHistory
{
    private readonly List<HeartRatePoint> _points = new();
    private readonly object _sync = new();
    public void Add(int bpm) { lock (_sync) _points.Add(new HeartRatePoint(DateTime.Now, bpm)); }
    public HeartRatePoint[] Snapshot() { lock (_sync) return _points.ToArray(); }
    public void Clear() { lock (_sync) _points.Clear(); }
}
