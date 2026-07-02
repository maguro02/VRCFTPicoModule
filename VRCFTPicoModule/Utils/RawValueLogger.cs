using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using VRCFTPicoModule.Maguro.Data;

namespace VRCFTPicoModule.Maguro.Utils;

public class RawValueLogger : IDisposable
{
    private readonly string _filePath;
    private readonly int _intervalMs;
    private readonly bool _includeVisemes;
    private readonly bool _visemesForcedOff;
    private readonly ILogger _logger;
    private readonly ConcurrentQueue<Row> _queue = new();
    private readonly ManualResetEventSlim _wake = new(false);
    private readonly CancellationTokenSource _cts = new();
    private readonly Thread _thread;
    private long _lastEnqueueTick;
    private bool _started;

    private readonly struct Row
    {
        public readonly long WallclockMs;
        public readonly float[] Shapes;
        public readonly float LeftOpenness;
        public readonly float LeftGazeX;
        public readonly float LeftGazeY;
        public readonly float RightOpenness;
        public readonly float RightGazeX;
        public readonly float RightGazeY;

        public Row(long ms, float[] shapes,
                   float lo, float lx, float ly,
                   float ro, float rx, float ry)
        {
            WallclockMs = ms; Shapes = shapes;
            LeftOpenness = lo; LeftGazeX = lx; LeftGazeY = ly;
            RightOpenness = ro; RightGazeX = rx; RightGazeY = ry;
        }
    }

    public RawValueLogger(string filePath, int intervalMs, bool includeVisemes, bool isLegacy, ILogger logger)
    {
        _filePath = filePath;
        _intervalMs = Math.Max(0, intervalMs);
        // Legacy packets only carry 52 shapes, so requesting visemes would produce rows with
        // fewer columns than the header. Force visemes off in that case (and warn once in Start()).
        _includeVisemes = includeVisemes && !isLegacy;
        _visemesForcedOff = includeVisemes && isLegacy;
        _logger = logger;
        _thread = new Thread(WriterLoop) { IsBackground = true, Name = "PicoRawValueLogger" };
    }

    public void Start()
    {
        try
        {
            if (_visemesForcedOff)
                _logger.LogWarning("log-include-visemes was requested but the legacy protocol only carries 52 shapes; visemes column will be omitted from the CSV.");

            var dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            WriteHeaderIfMissing();
            _thread.Start();
            _started = true;
            _logger.LogInformation("Raw value logger writing to {FilePath} (interval={IntervalMs} ms, visemes={IncludeVisemes})",
                _filePath, _intervalMs, _includeVisemes);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Failed to start raw value logger: {Message}", ex.Message);
        }
    }

    public void Enqueue(float[] shapes,
                        float leftOpenness, float leftGazeX, float leftGazeY,
                        float rightOpenness, float rightGazeX, float rightGazeY)
    {
        if (!_started || shapes.Length == 0) return;

        var nowMs = Environment.TickCount64;
        if (nowMs - Volatile.Read(ref _lastEnqueueTick) < _intervalMs) return;
        Volatile.Write(ref _lastEnqueueTick, nowMs);

        var snap = new float[shapes.Length];
        Array.Copy(shapes, snap, shapes.Length);
        _queue.Enqueue(new Row(nowMs, snap,
            leftOpenness, leftGazeX, leftGazeY,
            rightOpenness, rightGazeX, rightGazeY));
        _wake.Set();
    }

    private void WriteHeaderIfMissing()
    {
        if (File.Exists(_filePath) && new FileInfo(_filePath).Length > 0) return;

        var sb = new StringBuilder();
        sb.Append("wallclock_ms");
        var upper = _includeVisemes ? 72 : 52;
        for (var i = 0; i < upper; i++)
        {
            sb.Append(',').Append(((BlendShape.Index)i).ToString());
        }
        sb.Append(",Openness_L,GazeX_L,GazeY_L,Openness_R,GazeX_R,GazeY_R\n");
        File.WriteAllText(_filePath, sb.ToString());
    }

    private void WriterLoop()
    {
        try
        {
            using var stream = new FileStream(_filePath, FileMode.Append, FileAccess.Write, FileShare.Read);
            using var writer = new StreamWriter(stream, Encoding.UTF8);
            var flushCounter = 0;
            var upper = _includeVisemes ? 72 : 52;
            var ci = CultureInfo.InvariantCulture;

            while (!_cts.IsCancellationRequested)
            {
                try { _wake.Wait(1000, _cts.Token); }
                catch (OperationCanceledException) { break; }
                _wake.Reset();

                while (_queue.TryDequeue(out var row))
                {
                    writer.Write(row.WallclockMs.ToString(ci));
                    var limit = Math.Min(upper, row.Shapes.Length);
                    for (var i = 0; i < limit; i++)
                    {
                        writer.Write(',');
                        writer.Write(row.Shapes[i].ToString("F4", ci));
                    }
                    writer.Write(',');
                    writer.Write(row.LeftOpenness.ToString("F4", ci));
                    writer.Write(',');
                    writer.Write(row.LeftGazeX.ToString("F4", ci));
                    writer.Write(',');
                    writer.Write(row.LeftGazeY.ToString("F4", ci));
                    writer.Write(',');
                    writer.Write(row.RightOpenness.ToString("F4", ci));
                    writer.Write(',');
                    writer.Write(row.RightGazeX.ToString("F4", ci));
                    writer.Write(',');
                    writer.Write(row.RightGazeY.ToString("F4", ci));
                    writer.Write('\n');

                    if (++flushCounter >= 20)
                    {
                        writer.Flush();
                        flushCounter = 0;
                    }
                }
                writer.Flush();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Raw value logger writer thread stopped: {Message}", ex.Message);
        }
    }

    public void Dispose()
    {
        if (!_started)
        {
            _cts.Dispose();
            _wake.Dispose();
            return;
        }
        try
        {
            _cts.Cancel();
            _wake.Set();
            _thread.Join(500);
        }
        catch
        {
            // best-effort shutdown
        }
        _cts.Dispose();
        _wake.Dispose();
    }
}
