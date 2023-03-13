using System.Runtime.InteropServices;

namespace Pty.NET;

public class PtyTerminalHost<TConection> 
                                    :
                                        IAsyncDisposable
                                        , IDisposable
{
    private const uint _ctrlCExitCode = 0xC000013A;

    public PtyOptions? Options { get; init; }

    public readonly TConection Conection;

    private readonly string _consoleHost =
                                    RuntimeInformation
                                                .IsOSPlatform
                                                        (OSPlatform.Windows)
                                    ?
                                    Path.Combine(Environment.SystemDirectory, "cmd.exe")
                                    :
                                    "sh";

    private readonly CancellationTokenSource OnOutputCancellationTokenSource;

    private IPtyConnection? _terminal;

    private readonly TaskCompletionSource<uint> _processExitedTaskCompletionSource;

    private readonly CancellationTokenSource _readingCancellationTokenSource;


    public EventHandler<PtyExitedEventArgs>? OnProcessExited { get; set; }

    public PtyTerminalHost(TConection conection)
    {
        Conection = conection;
        _processExitedTaskCompletionSource = new TaskCompletionSource<uint>();
        OnOutputCancellationTokenSource = new CancellationTokenSource();
        _readingCancellationTokenSource = new CancellationTokenSource(100);
    }

    public async Task InputAsync(ArraySegment<byte> bytes, CancellationToken cancellationToken = default)
    {
        var buffer = bytes.ToArray()!;

        // skip start '/r', reserve end '/n'
        buffer = buffer.Skip(1).ToArray();

        await _terminal!
                    .WriterStream
                    .WriteAsync
                            (
                                buffer
                                , 0
                                , buffer.Length
                                , cancellationToken
                            );
        await _terminal!
                    .WriterStream
                    .FlushAsync
                            (cancellationToken);

    }


    public async Task StartRunAsync
                        (
                            Func
                                <
                                    PtyTerminalHost<TConection>
                                    , ArraySegment<byte>
                                    , Task
                                >
                                    onOutputProcessAsync
                            , int bufferBytesLength = 8 * 1024
                        )
    {
        if (_terminal is null)
        {
            Options!.App = _consoleHost;
            _terminal = await PtyProvider
                                    .SpawnAsync
                                            (
                                                Options!
                                                , OnOutputCancellationTokenSource.Token
                                            );
            _terminal.ProcessExited += (sender, e) =>
            {
                _processExitedTaskCompletionSource.TrySetResult((uint) _terminal.ExitCode);
                OnProcessExited?.Invoke(this, e);
            };
        }
        string output = string.Empty;
        var checkTerminalOutputAsync =
                Task
                    .Run
                        (
                            async () =>
                            {
                                var bytes = new byte[bufferBytesLength];
                                var listeningOutputCancellationToken =
                                            OnOutputCancellationTokenSource.Token;
                                while
                                    (
                                        !listeningOutputCancellationToken
                                                        .IsCancellationRequested
                                        &&
                                        !_processExitedTaskCompletionSource
                                                        .Task
                                                        .IsCompleted
                                    )
                                {
                                    int r = 0;
                                    do
                                    {
                                        try
                                        {
                                            r = await _terminal
                                                            .ReaderStream
                                                            .ReadAsync
                                                                    (
                                                                        bytes
                                                                        , 0
                                                                        , bytes.Length
                                                                        , _readingCancellationTokenSource.Token
                                                                    );
                                        }
                                        catch (IOException ioException)
                                        {
                                            Console.WriteLine($"Reading Caught {nameof(IOException)}:\r\n{ioException}");
                                            _readingCancellationTokenSource.Cancel();
                                            _isCanceledReading = true;
                                        }
                                        if 
                                            (
                                                _isCanceledReading
                                                &&
                                                _readingCancellationTokenSource
                                                                    .IsCancellationRequested
                                            )
                                        {
                                            break;
                                        }
                                        var reseted = false;
                                        while (!(reseted = _readingCancellationTokenSource.TryReset()));
                                    }
                                    while (r <= 0);

                                    if (r > 0)
                                    {
                                        ArraySegment<byte> buffer = new ArraySegment<byte>(bytes, 0, r);

                                        if (Conection != null)
                                        {
                                            await
                                                onOutputProcessAsync
                                                                    (
                                                                        this
                                                                        , buffer
                                                                    );
                                        }
                                    }
                                    if (_isCanceledReading)
                                    {
                                        break;
                                    }
                                }
                                return false;
                            }
                        );
    }

    public async Task<bool> ExitAsync()
    {
        bool r;
        while
            (
                !(r = await ExitOnceAsync())
            );
        return r;
    
    }

    private bool _isCanceledReading;

    private bool _isExited;
    public async Task<bool> ExitOnceAsync()
    {
        if (_isExited)
        {
            return
                _isExited;
        }

        var r = false;

        _readingCancellationTokenSource.Cancel();
        _isCanceledReading = true;

        var timeoutToken = OnOutputCancellationTokenSource.Token;
        using
            (
                timeoutToken
                            .Register
                                    (
                                        () =>
                                        {
                                            _processExitedTaskCompletionSource.TrySetCanceled(timeoutToken);
                                        }
                                    )
            )
        {
            _terminal!.Dispose();
            uint exitCode = await _processExitedTaskCompletionSource.Task;
            r =
                (
                    exitCode == _ctrlCExitCode      // WinPty terminal exit code.
                    ||
                    exitCode == 1                   // Pseudo Console exit code on Win 10.
                    ||
                    exitCode == 0                   // pty exit code on *nix.
                );
        }
        _isExited = r;
        _terminal!.WaitForExit(1000 * 10);
        return r;
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return default;
    }

    public void Dispose()
    {
        _ = !ExitAsync().Result;
        _terminal!.Dispose();
    }
}