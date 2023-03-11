using Pty.Net;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Pty.NET;

public class PtyTerminalHost<TConection> 
            : //IDisposable where TConection : IDisposable
                IAsyncDisposable  , IDisposable
{
    private const uint _ctrlCExitCode = 0xC000013A;

    public PtyOptions? Options { get; init; }

    public readonly TConection Conection;

    private readonly string _consoleHost = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? Path.Combine(Environment.SystemDirectory, "cmd.exe") : "sh";

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
                            Func<PtyTerminalHost<TConection>, ArraySegment<byte>, Task<bool>>
                                                onCheckedTerminalOutputProcessFuncAsync
                        )
    {
        if (Terminal is null)
        {
            Options!.App = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? Path.Combine(Environment.SystemDirectory, "cmd.exe") : "sh";
            Terminal = await PtyProvider.SpawnAsync(Options!, ListeningTerminalOutputCancellationTokenSource.Token);
            Terminal.ProcessExited += (sender, e) => _processExitedTcs.TrySetResult((uint)Terminal.ExitCode);
        }
        string output = string.Empty;
        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        var checkTerminalOutputAsync =
                Task
                    .Run
                        (
                            async () =>
                            {
                                var bytes = new byte[4096];
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
                                Console.WriteLine("while finished!");
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
                                $"Could not get any output from terminal{GetTerminalExitCode()}"
                                , exception
                            );
        };
    }

    public async Task<bool> ExitAsync()
    {
        ListeningTerminalOutputCancellationTokenSource.Cancel();
        var TimeoutToken = ListeningTerminalOutputCancellationTokenSource.Token;
        using (TimeoutToken.Register(() => _processExitedTcs.TrySetCanceled(TimeoutToken)))
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

    
    

