// See https://aka.ms/new-console-template for more information
using Pty.NET;
using System.Net;
using System.Net.Sockets;
using System.Text;

const string Data = "abc✓ЖЖЖ①Ⅻㄨㄩ 啊阿鼾齄丂丄狚狛狜狝﨨﨩ˊˋ˙– ⿻〇㐀㐁䶴䶵";
const int bytesBufferLength = 8 * 1024;
const string customExitCommandLine = "886";

TcpListener tcpListener = null!;
try
{
    int connectionId = 0;
    int connections = 0;

    _ = backgroundCommandLineAsync();

    #region Local Func backgroundCommandLineAsync
    async Task backgroundCommandLineAsync()
    {
        await
            Task
                .Run
                    (
                        () =>
                        {
                            Console.WriteLine($@"Interactive Command Line:");
                            Console.WriteLine($@"press ""q"" to exited interactive command line!");
                            Console.WriteLine($@"press any key to show current connections!");
                            var input = string.Empty;
                            while
                                (
                                    "q" != (input = Console.ReadLine())
                                )
                            {
                                Console.Write($"Current connections: [{connections}], max of connectionId: [{connectionId}] @ {DateTime.Now}");
                            }
                        }
                    );
        Console.WriteLine($"exited interactive command line!");
    }
    #endregion

    int port = 13000;
    tcpListener = new TcpListener(IPAddress.Any, port);

    // Start listening for client requests.
    tcpListener.Start();

    while (true)
    {
        Console.WriteLine($"Connections: [{connections}], Waiting for more connections ... @ {DateTime.Now}");
        TcpClient tcpClient = await tcpListener.AcceptTcpClientAsync();
        //Interlocked.Increment(ref connections);
        Console.WriteLine($"New connection: [{++ connectionId}] connected, current {nameof(connections)}: [{connections}] @ {DateTime.Now}");
        
        try
        {
            // run async
            _ = connectedProcessAsync();
            #region Local Func connectedProcessAsync
            async Task connectedProcessAsync()
            {
                try
                {
                    using var networkStream = tcpClient.GetStream();
                    await using var ptyTerminalHost =
                                        new PtyTerminalHost
                                                    <NetworkStream>
                                                            (networkStream, connectionId)
                    {
                        Options = new PtyOptions
                        {
                            Name = "Custom Terminal"
                            , Cols = Data.Length + Environment.CurrentDirectory.Length + 50
                            , Rows = 25
                            , Cwd = Environment.CurrentDirectory
                            , Environment = new Dictionary<string, string>()
                                {
                                      { "FOO", "bar" }
                                    , { "Bazz", string.Empty }
                                    ,
                                },
                        }
                    };

                    ptyTerminalHost.OnProcessExited += (sender, context) =>
                    {
                        if (networkStream is not null)
                        {
                            networkStream.Close();
                            networkStream.Dispose();
                        }
                        if (tcpClient is not null)
                        {
                            tcpClient.Close();
                            tcpClient.Dispose();
                            tcpClient = null!;
                        }
                        (PtyExitedEventArgs e, string Reason) = context;
                        PtyTerminalHost<NetworkStream>? t = (PtyTerminalHost<NetworkStream>) sender!;
                        Console
                            .WriteLine
                                (
                                    $"Event: {nameof(ptyTerminalHost.OnProcessExited)}: {sender!.GetType().Name}, exit {nameof(Reason)}: {Reason}, {nameof(e.ExitCode)}: {e.ExitCode}, {nameof(t.ConnectionId)}: {t!.ConnectionId}, {nameof(t.ProcessId)}: {t!.ProcessId}, {nameof(t.StartRunTime)}: {t.StartRunTime} @ {DateTime.Now}"
                                );
                    }; 

                    //ptyTerminalHost.OnProcessExited += (sender, (e) =>
                    //{
                    //    e.
                    //    if (networkStream is not null)
                    //    {
                    //        networkStream.Close();
                    //        networkStream.Dispose();
                    //    }

                    //    Console
                    //        .WriteLine
                    //            (
                    //                $"Event: {nameof(ptyTerminalHost.OnProcessExited)}: {sender!.GetType().Name}, {nameof(e.)}: {e.ExitCode} @ {DateTime.Now}"
                    //            );
                    //};

                    ptyTerminalHost.OnCaughtExceptionProcessAsync = async (sender, context, exception) =>
                    {
                        //Console.WriteLine($"On {nameof(context)}: {context}\r\nCaught Exception:\r\n{exception}");
                        return
                            await Task.FromResult(false);
                    };

                    // run async
                    _ = ptyTerminalHost
                                    .StartRunAsync
                                        (
                                            async (sender, data) =>
                                            {
                                                await sender
                                                            .Conection
                                                            .WriteAsync
                                                                    (data);
                                            }
                                        );

                    var p = 0;
                    var bytes = new byte[bytesBufferLength];
                    var timeoutToken = new CancellationTokenSource().Token;

                    while (1 == 1)
                    {
                        //Console.WriteLine($"socket reading ... @ {DateTime.Now}");
                        int r = networkStream.ReadByte();
                        if (r < 0)
                        {
                            Thread.Sleep(100);
                            continue;
                        }
                        byte b = (byte)r;
                        char c = (char)r;
                        if
                            (
                                c != '\n'    // enter
                                //&&
                                //b != 0x27
                                &&
                                !char.IsControl(c)
                            )
                        {
                            // Console.WriteLine($"socket writing {c} @ {DateTime.Now}");
                            networkStream.WriteByte((byte)'\b');
                            networkStream.WriteByte(b);
                        }
                        var buffer = new byte[] { b };
                        var l = buffer.Length;
                        Buffer.BlockCopy(buffer, 0, bytes, p, l);
                        p += l;

                        if
                            (
                                c == (byte)'\n'    // enter
                                //||
                                //b == 0x26         // up
                                //||
                                //b == 0x28         // down
                            )
                        {
                            ArraySegment<byte> arraySegment = new ArraySegment<byte>(bytes, 0, p);
                            var commandLine = Encoding.UTF8.GetString(arraySegment).Trim();
                            Console.WriteLine($"Connection [{connectionId}] Receive Command Line:\r\n{commandLine}\r\n@ {DateTime.Now}");
                            if (commandLine == customExitCommandLine)
                            {
                                // run async
                                await networkStream.WriteAsync(arraySegment);
                                await ptyTerminalHost.ExitOnceAsync();
                                break;
                            }
                            await ptyTerminalHost.InputAsync(arraySegment);
                            p = 0;
                        }
                    };
                    if (networkStream is not null)
                    {
                        networkStream.Close();
                        networkStream.Dispose();
                    }
                }
                finally
                {
                    if (tcpClient is not null)
                    {
                        tcpClient.Close();
                        tcpClient.Dispose();
                        tcpClient = null!;
                    }
                    //Interlocked.Decrement(ref connections);
                    Console.WriteLine($"Connection: [{connectionId}] closed, remain {nameof(connections)}: [{connections}] @ {DateTime.Now}");
                }
            }
            #endregion
        }
        catch (Exception e)
        {
            Console.WriteLine($"Caught Exception:\r\n{e}");
        }
    }
}
finally
{
    tcpListener.Stop();
    tcpListener = null!;
}


