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
        string app;
        string[] args;
        if (profile is "Codex" or "CodexResume" or "Claude")
        {
            var tool = profile == "Claude" ? "claude" : "codex";
            app = FindExecutable(tool) ?? throw new FileNotFoundException($"{tool} غير موجود في PATH. ثبّته أولًا ثم أعد فتح Satr.");
            args = profile == "CodexResume" ? ["resume"] : [];
            if (OperatingSystem.IsWindows() && Path.GetExtension(app) is ".cmd" or ".bat")
            {
                // Only fixed tool names enter cmd syntax; the project path travels in Cwd.
                app = Path.Combine(Environment.SystemDirectory, "cmd.exe");
                args = ["/D", "/S", "/C", tool + (profile == "CodexResume" ? " resume" : "")];
            }
        }
        else if (OperatingSystem.IsWindows())
        {
            app = FindExecutable("pwsh") ?? Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
            args = ["-NoLogo"];
        }
        else
        {
            var shell = Environment.GetEnvironmentVariable("SHELL");
            app = shell is not null && Path.IsPathFullyQualified(shell) && File.Exists(shell) ? shell : "/bin/bash";
            args = ["-i"];
        }
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
        if (_disposed) throw new IOException("الجلسة مغلقة.");
        if (text.Length > 1024 * 1024 + 32) throw new IOException("النص أكبر من حد اللصق 1 MB.");
        if (!_input.Writer.TryWrite(Encoding.UTF8.GetBytes(text))) throw new IOException("الجلسة لا تستقبل المدخلات حاليًا. النص بقي في المحرر؛ حاول مجددًا.");
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
        catch (Exception ex) { if (!_disposed) Ended?.Invoke("تعذّرت الكتابة: " + ex.Message); }
    }

    private async Task ReadLoop()
    {
        var decoder = Encoding.UTF8.GetDecoder();
        var bytes = new byte[32768];
        var chars = new char[32769];
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                var count = await _pty.ReaderStream.ReadAsync(bytes, _stop.Token);
                if (count == 0) break;
                var length = decoder.GetChars(bytes, 0, count, chars, 0);
                if (length > 0 && !_disposed) Output?.Invoke(new string(chars, 0, length));
            }
            if (!_disposed) Ended?.Invoke("انتهت الجلسة — يمكنك فتح جلسة جديدة.");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!_disposed) Ended?.Invoke("توقّفت الجلسة: " + ex.Message); }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _input.Writer.TryComplete();
        // Keep draining output while ConPTY closes; stopping the reader first can
        // deadlock ClosePseudoConsole when the child's output pipe is full.
        await Task.Run(_pty.Dispose);
        _stop.Cancel();
        await Task.WhenAll(_reader ?? Task.CompletedTask, _writer);
        _stop.Dispose();
    }
}
