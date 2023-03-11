// See https://aka.ms/new-console-template for more information
using Pty.Net;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Text;
using System.Diagnostics;
using System.Net.Sockets;
using System.Net;

Console.WriteLine("Hello, World!");


var aa = new byte[]
            { 1, 2, 3, 4, 15, 14, 15 }
                .TakeTopFirst
                    (
                        (x) =>
                        {
                            return x == 0x0D;
                        }
                    ).ToArray();

//return;

const uint CtrlCExitCode = 0xC000013A;

int TestTimeoutMs = Debugger.IsAttached ? 300_0000 : 5_000;

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
                                                            { 
                                                                "FOO"
                                                                , "bar" 
                                                            }
                                                        , 
                                                            {
                                                                "Bazz"
                                                                , string.Empty 
                                                            }
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

NetworkStream networkStream = null!;

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

                                if (networkStream != null)
                                { 
                                    await networkStream.WriteAsync( buffer , 0 , count);
                                }


                                if (count == 0)
                                {
                                    //break;
                                }

                                firstOutput.TrySetResult(null);

                                output += encoding.GetString(buffer, 0, count);
                                output = output
                                                .Replace("\r", string.Empty)
                                                .Replace("\n", string.Empty);
                                output = ansiRegex.Replace(output, string.Empty);

                                Console.WriteLine( output );

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
                            Console.WriteLine("while finished!");
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
};

try
{
    TcpListener tcpListener = null!;
    try
    {
        int port = 13000;
        tcpListener = new TcpListener(IPAddress.Any, port);

        // Start listening for client requests.
        tcpListener.Start();

        // Buffer for reading data

        // Enter the listening loop.
        while (true)
        {
            Console.Write("Waiting for a connection... ");
            Thread.Sleep(100);

            // Perform a blocking call to accept requests.
            // You could also use server.AcceptSocket() here.
            using TcpClient tcpClient = tcpListener.AcceptTcpClient();
            Console.WriteLine("Connected!");

            // Get a stream object for reading and writing
            networkStream = tcpClient.GetStream();

            
            var p = 0;
            var bytes = new byte[64 * 1024];

            while (1 == 1)
            {
                Console.WriteLine($"socket reading ... @ {DateTime.Now}");
                int r = networkStream.ReadByte();
                if (r < 0)
                {
                    Thread.Sleep(100);
                    continue;
                }
                byte b = (byte) r;
                if (r != 0x0D)
                {
                    Console.WriteLine($"socket writing {b} ... @ {DateTime.Now}");
                    networkStream.WriteByte((byte) '\b');
                    networkStream.WriteByte(b);
                }
                //continue;
                var buffer = new byte[] { b };
                var l = buffer.Length;
                Buffer.BlockCopy(buffer, 0, bytes, p, l);
                p += l;

                if (r == 0x0D)
                {
                    await terminal.WriterStream.WriteAsync(bytes, 0, p, TimeoutToken);
                    await terminal.WriterStream.FlushAsync(TimeoutToken);

                    p = 0;
                }
            }
            //Console.WriteLine($"socket reading finished!!! @ {DateTime.Now}");
        }
    }
    //catch (SocketException e)
    //{
    //    Console.WriteLine("SocketException: {0}", e);
    //}
    finally
    {
        tcpListener.Stop();
    }
    //FakeAssert.True(await checkTerminalOutputAsync);
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
Console.ReadLine();


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


public static class LinqHelper
{ 
    public static IEnumerable<T>
                                TakeTopFirst<T>
                                    (
                                        this IEnumerable<T> @this
                                        , Func<T, bool> predicate
                                    )
    {
        foreach (var t in @this)
        {
            yield return t;
            if (predicate(t))
            {
                break;            
            }
        }
    }
}
