namespace HeartRateAntPlus;

public sealed record HeartRatePoint(DateTime Timestamp, int Bpm);

public sealed class HeartRateStatistics
{
    private long _count;
    private long _sum;
    private int _minimum = int.MaxValue;
    private int _maximum = int.MinValue;

    public int Count => (int)Math.Min(_count, int.MaxValue);
    public bool HasValue => _count > 0;
    public int Minimum => _minimum;
    public int Maximum => _maximum;
    public double Average => _count == 0 ? 0 : (double)_sum / _count;

    public void Add(int bpm)
    {
        _count++;
        _sum += bpm;
        _minimum = Math.Min(_minimum, bpm);
        _maximum = Math.Max(_maximum, bpm);
    }

    public void Clear()
    {
        _count = 0;
        _sum = 0;
        _minimum = int.MaxValue;
        _maximum = int.MinValue;
    }
}

public sealed class HeartRateHistory
{
    private sealed class Bucket
    {
        public DateTime Start;
        public DateTime FirstAt;
        public DateTime LastAt;
        public DateTime MinAt;
        public DateTime MaxAt;
        public int First;
        public int Last;
        public int Min;
        public int Max;

        public Bucket(DateTime timestamp, int bpm)
        {
            Start = timestamp;
            FirstAt = LastAt = MinAt = MaxAt = timestamp;
            First = Last = Min = Max = bpm;
        }

        public void Add(DateTime timestamp, int bpm)
        {
            if (timestamp < FirstAt) { FirstAt = timestamp; First = bpm; }
            if (timestamp >= LastAt) { LastAt = timestamp; Last = bpm; }
            if (bpm < Min) { Min = bpm; MinAt = timestamp; }
            if (bpm > Max) { Max = bpm; MaxAt = timestamp; }
        }

        public void Merge(Bucket other)
        {
            if (other.FirstAt < FirstAt) { FirstAt = other.FirstAt; First = other.First; }
            if (other.LastAt > LastAt) { LastAt = other.LastAt; Last = other.Last; }
            if (other.Min < Min) { Min = other.Min; MinAt = other.MinAt; }
            if (other.Max > Max) { Max = other.Max; MaxAt = other.MaxAt; }
        }
    }

    private readonly List<Bucket> _buckets = new();
    private readonly object _sync = new();
    private DateTime? _firstTimestamp;
    private int _bucketSeconds = 1;

    public int Count { get { lock (_sync) return _buckets.Count; } }
    public void Add(int bpm) => Add(DateTime.Now, bpm);

    private void Add(DateTime timestamp, int bpm)
    {
        lock (_sync)
        {
            _firstTimestamp ??= timestamp;
            var newBucketSeconds = ChooseBucketSeconds(timestamp - _firstTimestamp.Value);
            if (newBucketSeconds != _bucketSeconds)
            {
                _bucketSeconds = newBucketSeconds;
                Rebucket();
            }

            var bucketStart = AlignToBucket(timestamp, _bucketSeconds);
            var bucket = _buckets.Count > 0 ? _buckets[^1] : null;
            if (bucket is null || bucket.Start != bucketStart)
            {
                bucket = new Bucket(timestamp, bpm) { Start = bucketStart };
                _buckets.Add(bucket);
            }
            else bucket.Add(timestamp, bpm);
        }
    }

    public HeartRatePoint[] Snapshot()
    {
        lock (_sync)
        {
            var result = new List<HeartRatePoint>(_buckets.Count * 4);
            foreach (var bucket in _buckets)
            {
                AddIfNew(result, new HeartRatePoint(bucket.FirstAt, bucket.First));
                AddIfNew(result, new HeartRatePoint(bucket.MinAt, bucket.Min));
                AddIfNew(result, new HeartRatePoint(bucket.MaxAt, bucket.Max));
                AddIfNew(result, new HeartRatePoint(bucket.LastAt, bucket.Last));
            }
            return result.OrderBy(x => x.Timestamp).ToArray();
        }
    }

    public void Clear()
    {
        lock (_sync)
        {
            _buckets.Clear();
            _firstTimestamp = null;
            _bucketSeconds = 1;
        }
    }

    private void Rebucket()
    {
        if (_buckets.Count < 2) return;
        var rebucketed = new List<Bucket>();
        foreach (var old in _buckets)
        {
            var start = AlignToBucket(old.Start, _bucketSeconds);
            var target = rebucketed.Count > 0 && rebucketed[^1].Start == start ? rebucketed[^1] : null;
            if (target is null)
            {
                target = new Bucket(old.FirstAt, old.First) { Start = start };
                rebucketed.Add(target);
            }
            target.Merge(old);
        }
        _buckets.Clear();
        _buckets.AddRange(rebucketed);
    }

    private static void AddIfNew(List<HeartRatePoint> points, HeartRatePoint point)
    {
        if (points.Count == 0 || points[^1] != point) points.Add(point);
    }

    private static int ChooseBucketSeconds(TimeSpan elapsed) => elapsed.TotalMinutes <= 10 ? 1
        : elapsed.TotalHours <= 1 ? 2
        : elapsed.TotalHours <= 3 ? 5
        : elapsed.TotalHours <= 12 ? 15
        : elapsed.TotalHours <= 24 ? 30
        : 60;

    private static DateTime AlignToBucket(DateTime timestamp, int seconds)
    {
        var ticks = timestamp.Ticks - timestamp.Ticks % TimeSpan.FromSeconds(seconds).Ticks;
        return new DateTime(ticks, timestamp.Kind);
    }
}
