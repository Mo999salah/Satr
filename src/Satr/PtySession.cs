using Porta.Pty;
using System.Text;
using System.Threading.Channels;

namespace Satr;

internal sealed class PtySession : IAsyncDisposable
{
    private readonly IPtyConnection _pty;
    private readonly CancellationTokenSource _stop = new();
    // Bounded input prevents an unresponsive CLI from accumulating unlimited pasted text.
    private readonly Channel<byte[]> _input = Channel.CreateBounded<byte[]>(32);
    private readonly Task _writer;
    private Task? _reader;
    private volatile bool _disposed;
    private Task? _disposeTask;
    public event Action<string>? Warning;
    public event Action<string>? Output;
    public event Action<string>? Ended;

    private PtySession(IPtyConnection pty)
    {
        _pty = pty;
        _writer = WriteLoop();
    }

    public static async Task<PtySession> Start(string profile, string directory, int columns, int rows, CancellationToken token)
    {
        if (!Directory.Exists(directory)) throw new DirectoryNotFoundException(directory);
        var plan = ProfileCatalog.ResolveLaunch(profile, FindExecutable);
        var app = plan.App;
        var args = plan.Arguments;
        var environment = new Dictionary<string, string> { ["TERM"] = "xterm-256color", ["COLORTERM"] = "truecolor" };
        if (!OperatingSystem.IsWindows() && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("LANG")))
            environment["LANG"] = "C.UTF-8";
        var pty = await PtyProvider.SpawnAsync(new PtyOptions
        {
            Name = "Satr", App = app, CommandLine = args, Cwd = directory,
            Cols = columns, Rows = rows, Environment = environment
        }, token);
        if (token.IsCancellationRequested) { pty.Dispose(); token.ThrowIfCancellationRequested(); }
        return new PtySession(pty);
    }

    internal static string? FindExecutable(string name)
    {
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(directory)) continue;
            foreach (var suffix in OperatingSystem.IsWindows() ? new[] { ".exe", ".cmd", ".bat" } : new[] { "" })
            {
                var path = Path.Combine(directory.Trim('"'), name + suffix);
                if (!File.Exists(path)) continue;
                if (OperatingSystem.IsWindows() || (File.GetUnixFileMode(path) &
                    (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0)
                    return Path.GetFullPath(path);
            }
        }
        return null;
    }

    public void ReadOutput() => _reader ??= Task.Run(ReadLoop);

    public void Write(string text)
    {
        if (_disposed) throw new IOException("Session closed.");
        if (text.Length > 1024 * 1024 + 32) throw new IOException("Text exceeds the 1 MB paste limit.");
        if (!_input.Writer.TryWrite(Encoding.UTF8.GetBytes(text))) throw new IOException("Session not accepting input right now. This batch was not sent; try again.");
    }

    public void WriteResponses(IReadOnlyList<string> responses)
    {
        if (_disposed) return;
        // One batch avoids filling the queue with individual capability replies.
        var text = string.Concat(responses);
        if (text.Length > 1024 * 1024 || !_input.Writer.TryWrite(Encoding.UTF8.GetBytes(text)))
            Warning?.Invoke("Terminal response queue is full. A capability reply could not be delivered; reopen the session if the tool is waiting.");
    }

    public void Resize(int columns, int rows) { if (!_disposed) _pty.Resize(columns, rows); }

    private async Task WriteLoop()
    {
        try
        {
            await foreach (var bytes in _input.Reader.ReadAllAsync(_stop.Token))
            {
                await _pty.WriterStream.WriteAsync(bytes, _stop.Token);
                await _pty.WriterStream.FlushAsync(_stop.Token);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!_disposed) Ended?.Invoke("Write failed: " + ex.Message); }
    }

    private async Task ReadLoop()
    {
        var decoder = new Utf8OutputDecoder();
        var bytes = new byte[32768];
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                var count = await _pty.ReaderStream.ReadAsync(bytes, _stop.Token);
                if (count == 0) break;
                var text = decoder.Decode(bytes.AsSpan(0, count));
                if (text.Length > 0 && !_disposed) Output?.Invoke(text);
            }
            var tail = decoder.Decode([], flush: true);
            if (tail.Length > 0 && !_disposed) Output?.Invoke(tail);
            if (!_disposed) Ended?.Invoke("Session ended — you can open a new session.");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!_disposed) Ended?.Invoke("Session stopped: " + ex.Message); }
    }

    internal static readonly TimeSpan DisposeBudget = TimeSpan.FromSeconds(5);

    public ValueTask DisposeAsync()
    {
        lock (_stop) return new ValueTask(_disposeTask ??= DisposeCore());
    }

    private async Task DisposeCore()
    {
        _disposed = true;
        _input.Writer.TryComplete();
        // Keep draining output while ConPTY closes; stopping the reader first can
        // deadlock ClosePseudoConsole when the child's output pipe is full.
        try
        {
            await AwaitBounded(Task.Run(() =>
            {
                if (!_pty.WaitForExit(0)) _pty.Kill();
                if (!_pty.WaitForExit((int)DisposeBudget.TotalMilliseconds))
                    throw new TimeoutException("The terminal process did not exit.");
                _pty.Dispose();
            }), DisposeBudget + DisposeBudget);
        }
        finally { _stop.Cancel(); }
        await AwaitBounded(Task.WhenAll(_reader ?? Task.CompletedTask, _writer), DisposeBudget);
        _stop.Dispose();
    }

    internal static async Task AwaitBounded(Task task, TimeSpan budget)
    {
        await task.WaitAsync(budget);
    }
}
