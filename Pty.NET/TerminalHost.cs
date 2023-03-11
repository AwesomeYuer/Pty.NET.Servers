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

    public readonly CancellationTokenSource ListeningTerminalOutputCancellationTokenSource;

    public IPtyConnection? Terminal { get; private set; }

    private readonly TaskCompletionSource<uint> _processExitedTcs;

    private readonly TaskCompletionSource<object?> _firstOutput;

    private readonly TaskCompletionSource<object?> _firstDataFound;

    private string GetTerminalExitCode() =>
                            (
                                _processExitedTcs.Task.IsCompleted
                                ?
                                $". Terminal process has exited with exit code {_processExitedTcs.Task.GetAwaiter().GetResult()}."
                                :
                                string.Empty
                            );

    public PtyTerminalHost(TConection conection)
    {
        Conection = conection;
        _processExitedTcs = new TaskCompletionSource<uint>();
        ListeningTerminalOutputCancellationTokenSource = new CancellationTokenSource();
        _firstOutput = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _firstDataFound = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
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
                        )
    {
        if (Terminal is null)
        {
            Options!.App = _consoleHost;
            Terminal = await PtyProvider
                                    .SpawnAsync
                                            (
                                                Options!
                                                , ListeningTerminalOutputCancellationTokenSource
                                                                                                .Token
                                            );
            Terminal.ProcessExited += (sender, e) => _processExitedTcs.TrySetResult((uint)Terminal.ExitCode);
        }
        string output = string.Empty;
        var checkTerminalOutputAsync =
                Task
                    .Run
                        (
                            async () =>
                            {
                                var bytes = new byte[64 * 1024];
                                var runningCancellationToken = ListeningTerminalOutputCancellationTokenSource.Token;
                                while
                                    (
                                        !ListeningTerminalOutputCancellationTokenSource.Token.IsCancellationRequested
                                        &&
                                        !_processExitedTcs.Task.IsCompleted
                                    )
                                {
                                    int r =
                                            await Terminal
                                                        .ReaderStream
                                                        .ReadAsync
                                                                (
                                                                    bytes
                                                                    , 0
                                                                    , bytes.Length
                                                                    , runningCancellationToken
                                                                );

                                    ArraySegment<byte> buffer = new ArraySegment<byte>(bytes, 0, r);

                                    if (Conection != null)
                                    {
                                        _ = await onCheckedTerminalOutputProcessFuncAsync
                                                        (
                                                            this
                                                            , buffer
                                                        );
                                    }
                                    _firstOutput.TrySetResult(null);
                                }
                                _firstOutput.TrySetCanceled();
                                _firstDataFound.TrySetCanceled();
                                return false;
                            }
                        );
        try
        {
            await _firstOutput.Task;
        }
        catch (OperationCanceledException exception)
        {
            throw
                new InvalidOperationException
                            (
                                $"Could not get any output from terminal {GetTerminalExitCode()}"
                                , exception
                            );
        };
    }

    public async Task<bool> ExitAsync()
    {
        ListeningTerminalOutputCancellationTokenSource.Cancel();
        var timeoutToken = ListeningTerminalOutputCancellationTokenSource.Token;
        using (timeoutToken.Register(() => _processExitedTcs.TrySetCanceled(timeoutToken)))
        {
            uint exitCode = await _processExitedTcs.Task;
            return
                (
                    exitCode == _ctrlCExitCode   // WinPty terminal exit code.
                    ||
                    exitCode == 1               // Pseudo Console exit code on Win 10.
                    ||
                    exitCode == 0               // pty exit code on *nix.
                );
        }
    }

    public void Dispose()
    {
        ExitAsync().Wait();
        Terminal!.Dispose();
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return default;
    }
}