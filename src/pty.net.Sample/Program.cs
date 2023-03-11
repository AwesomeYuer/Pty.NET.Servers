// See https://aka.ms/new-console-template for more information
using Pty.Net;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Text;
using System.Diagnostics;

Console.WriteLine("Hello, World!");

const uint CtrlCExitCode = 0xC000013A;

int TestTimeoutMs = Debugger.IsAttached ? 300_000 : 5_000;

CancellationToken TimeoutToken = new CancellationTokenSource(TestTimeoutMs).Token;



var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
const string Data = "abc✓ЖЖЖ①Ⅻㄨㄩ 啊阿鼾齄丂丄狚狛狜狝﨨﨩ˊˋ˙– ⿻〇㐀㐁䶴䶵";

string app = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? Path.Combine(Environment.SystemDirectory, "cmd.exe") : "sh";
var options = new PtyOptions
                        {
                              Name = "Custom terminal"
                            , Cols = Data.Length + Environment.CurrentDirectory.Length + 50
                            , Rows = 25
                            , Cwd = Environment.CurrentDirectory
                            , App = app
                            , Environment = new Dictionary<string, string>()
                                                    {
                                                          { "FOO", "bar" }
                                                        , { "Bazz", string.Empty }
                                                        ,
                                                    },
                        };

IPtyConnection terminal = await PtyProvider.SpawnAsync(options, TimeoutToken);

var processExitedTcs = new TaskCompletionSource<uint>();
terminal.ProcessExited += (sender, e) => processExitedTcs.TrySetResult((uint)terminal.ExitCode);

string GetTerminalExitCode() =>
                            (
                                processExitedTcs.Task.IsCompleted
                                ? 
                                $". Terminal process has exited with exit code {processExitedTcs.Task.GetAwaiter().GetResult()}."
                                :
                                string.Empty
                            );

var firstOutput = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
var firstDataFound = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
var output = string.Empty;
var checkTerminalOutputAsync =
            Task
                .Run
                    (
                        async () =>
                        {
                            var buffer = new byte[4096];
                            var ansiRegex =
                                        new Regex
                                                (
                                                    @"[\u001B\u009B][[\]()#;?]*(?:(?:(?:[a-zA-Z\d]*(?:;[a-zA-Z\d]*)*)?\u0007)|(?:(?:\d{1,4}(?:;\d{0,4})*)?[\dA-PRZcf-ntqry=><~]))"
                                                );

                            while 
                                (
                                    !TimeoutToken.IsCancellationRequested
                                    &&
                                    !processExitedTcs.Task.IsCompleted
                                )
                            {
                                int count =
                                        await terminal
                                                    .ReaderStream
                                                    .ReadAsync
                                                            (
                                                                buffer
                                                                , 0
                                                                , buffer.Length
                                                                , TimeoutToken
                                                            );
                                if (count == 0)
                                {
                                    break;
                                }

                                firstOutput.TrySetResult(null);

                                output += encoding.GetString(buffer, 0, count);
                                output = output
                                                .Replace("\r", string.Empty)
                                                .Replace("\n", string.Empty);
                                output = ansiRegex.Replace(output, string.Empty);

                                var index = output.IndexOf(Data);
                                if (index >= 0)
                                {
                                    firstDataFound.TrySetResult(null);
                                    if 
                                        (
                                            index <= output.Length - (2 * Data.Length)
                                            &&
                                            output.IndexOf(Data, index + Data.Length) >= 0
                                        )
                                    {
                                        return true;
                                    }
                                }
                            }

                            firstOutput.TrySetCanceled();
                            firstDataFound.TrySetCanceled();
                            return false;
                        }
                    );

try
{
    await firstOutput.Task;
}
catch (OperationCanceledException exception)
{
    throw
        new InvalidOperationException
                    (
                        $"Could not get any output from terminal{GetTerminalExitCode()}"
                        , exception
                    );
}

try
{
    byte[] commandBuffer = encoding.GetBytes("echo " + Data);
    await terminal.WriterStream.WriteAsync(commandBuffer, 0, commandBuffer.Length, TimeoutToken);
    await terminal.WriterStream.FlushAsync();

    await firstDataFound.Task;

    await terminal.WriterStream.WriteAsync(new byte[] { 0x0D }, 0, 1, TimeoutToken); // Enter
    await terminal.WriterStream.FlushAsync();

    FakeAssert.True(await checkTerminalOutputAsync);
}
catch (Exception exception)
{
    throw new InvalidOperationException
                        (
                            $"Could not get expected data from terminal.{GetTerminalExitCode()} Actual terminal output:\n{output}"
                            , exception
                        );
}

terminal.Resize(40, 10);

terminal.Dispose();

using (TimeoutToken.Register(() => processExitedTcs.TrySetCanceled(TimeoutToken)))
{
    uint exitCode = await processExitedTcs.Task;
    FakeAssert
            .True
                (
                    exitCode == CtrlCExitCode   // WinPty terminal exit code.
                    ||
                    exitCode == 1               // Pseudo Console exit code on Win 10.
                    ||
                    exitCode == 0               // pty exit code on *nix.
                );
}

FakeAssert.True(terminal.WaitForExit(TestTimeoutMs));

Console.WriteLine("Finished!!!");
//Console.ReadLine();


public static class FakeAssert
{
    public static bool True(bool condition)
    {
        if (!condition)
        {
            throw new Exception($"{nameof(FakeAssert)}.{nameof(FakeAssert.True)} is failed!");
        }
        return condition;
    }
}