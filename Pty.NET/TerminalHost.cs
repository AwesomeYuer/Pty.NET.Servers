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

    public readonly CancellationTokenSource ListeningOutputCancellationTokenSource;

    public IPtyConnection? Terminal { get; private set; }

    private readonly TaskCompletionSource<uint> _processExitedTaskCompletionSource;

    private readonly CancellationTokenSource _readingCancellationTokenSource;

    public PtyTerminalHost(TConection conection)
    {
        Conection = conection;
        _processExitedTaskCompletionSource = new TaskCompletionSource<uint>();
        ListeningOutputCancellationTokenSource = new CancellationTokenSource();
        _readingCancellationTokenSource = new CancellationTokenSource(100);
    }

    public async Task StartListenTerminalOutputAsync
                        (
                            Func
                                <
                                    PtyTerminalHost<TConection>
                                    , ArraySegment<byte>
                                    , Task<bool>
                                >
                                    onCheckedTerminalOutputProcessFuncAsync
                            , int bytesBufferLength = 8 * 1024
                        )
    {
        if (Terminal is null)
        {
            Options!.App = _consoleHost;
            Terminal = await PtyProvider
                                    .SpawnAsync
                                            (
                                                Options!
                                                , ListeningOutputCancellationTokenSource.Token
                                            );
            Terminal.ProcessExited += (sender, e) => _processExitedTaskCompletionSource.TrySetResult((uint) Terminal.ExitCode);
        }
        string output = string.Empty;
        var checkTerminalOutputAsync =
                Task
                    .Run
                        (
                            async () =>
                            {
                                var bytes = new byte[bytesBufferLength];
                                var listeningOutputCancellationToken =
                                            ListeningOutputCancellationTokenSource.Token;
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
                                            r = await Terminal
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
                                            _ = await onCheckedTerminalOutputProcessFuncAsync
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

        var timeoutToken = ListeningOutputCancellationTokenSource.Token;
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
            Terminal!.Dispose();
            uint exitCode = await _processExitedTaskCompletionSource.Task;
            r =
                (
                    exitCode == _ctrlCExitCode   // WinPty terminal exit code.
                    ||
                    exitCode == 1               // Pseudo Console exit code on Win 10.
                    ||
                    exitCode == 0               // pty exit code on *nix.
                );
        }
        _isExited = r;
        Terminal!.WaitForExit(1000 * 10);
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
        Terminal!.Dispose();
    }
}