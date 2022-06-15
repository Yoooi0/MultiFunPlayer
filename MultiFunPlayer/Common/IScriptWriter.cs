using System.Globalization;
using System.IO;
using System.Text;

namespace MultiFunPlayer.Common;

public interface IScriptWriter : IDisposable
{
    void Write(float position, float value);
    void Write(Keyframe keyframe) => Write(keyframe.Position, keyframe.Value);
    void Write(IEnumerable<Keyframe> keyframes)
    {
        foreach (var keyframe in keyframes)
            Write(keyframe);
    }
}

public class FunscriptWriter : IScriptWriter
{
    private readonly Stream _stream;
    private bool _isFirst;

    public FunscriptWriter(string outputPath)
    {
        ArgumentNullException.ThrowIfNull(outputPath, nameof(outputPath));

        _stream = new FileStream(outputPath, FileMode.CreateNew);
        _isFirst = true;

        Write("{ \"actions\": [");
    }

    public void Write(float position, float value)
    {
        if (!_isFirst)
            Write(",");

        var at = (int)Math.Round(position * 1000);
        var pos = MathUtils.Clamp(value * 100, 0, 100);
        Write($"{{\"at\":{at.ToString(CultureInfo.InvariantCulture)},\"pos\":{pos.ToString(CultureInfo.InvariantCulture)}}}");

        _isFirst = false;
    }

    private void Write(string s) => _stream.Write(Encoding.UTF8.GetBytes(s));

    protected virtual void Dispose(bool disposing)
    {
        Write("]}");

        _stream.Flush();
        _stream.Dispose();
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}

public class CsvWriter : IScriptWriter
{
    private readonly Stream _stream;

    public CsvWriter(string outputPath)
    {
        ArgumentNullException.ThrowIfNull(outputPath, nameof(outputPath));

        _stream = new FileStream(outputPath, FileMode.CreateNew);
        Write("position;value\n");
    }

    public void Write(float position, float value)
        => Write($"{position.ToString(CultureInfo.InvariantCulture)};{value.ToString(CultureInfo.InvariantCulture)}\n");

    private void Write(string s) => _stream.Write(Encoding.UTF8.GetBytes(s));

    protected virtual void Dispose(bool disposing)
    {
        _stream.Flush();
        _stream.Dispose();
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}