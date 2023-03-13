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
    int port = 13000;
    tcpListener = new TcpListener(IPAddress.Any, port);

    // Start listening for client requests.
    tcpListener.Start();

    int i = 0;

    while (true)
    {
        Console.WriteLine($"Connected: [{i}], Waiting for more connections ... @ {DateTime.Now}");
        TcpClient tcpClient = await tcpListener.AcceptTcpClientAsync();
        Console.WriteLine($"new Connect: [{++ i}] @ {DateTime.Now}");

        try
        {
            _ = ProcessAsync();

            #region Local Func ProcessAsync
            async Task ProcessAsync()
            {
                try
                {
                    using var networkStream = tcpClient.GetStream();
                    await using var ptyTerminalHost = new PtyTerminalHost<NetworkStream>(networkStream)
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

                    ptyTerminalHost.OnProcessExited += (sender, e) =>
                    {
                        Console.WriteLine($"{nameof(ptyTerminalHost.OnProcessExited)}: {sender!.GetType().Name} , {e.ExitCode} @ {DateTime.Now}");
                    };

                    ptyTerminalHost.OnCaughtExceptionProcessAsync = async (sender, context, exception) =>
                    {
                        Console.WriteLine($"On {nameof(context)}: {context}\r\nCaught Exception:\r\n{exception}");
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
                        byte b = (byte) r;
                        char c = (char) r;
                        if
                            (
                                b != 0x0D
                                //&&
                                //b != 0x27
                                &&
                                !char.IsControl(c)
                            )
                        {
                            // Console.WriteLine($"socket writing {c} @ {DateTime.Now}");
                            networkStream.WriteByte((byte) '\b');
                            networkStream.WriteByte(b);
                        }
                        var buffer = new byte[] { b };
                        var l = buffer.Length;
                        Buffer.BlockCopy(buffer, 0, bytes, p, l);
                        p += l;

                        if
                            (
                                b == 0x0D   // enter
                                //||
                                //b == 0x26   // up
                                //||
                                //b == 0x28   // down
                            )
                        {
                            ArraySegment<byte> arraySegment = new ArraySegment<byte>(bytes, 0, p);
                            var commandLine = Encoding.UTF8.GetString(arraySegment).Trim();
                            Console.WriteLine($"Connection [{i}] Receive Command Line:\r\n{commandLine}\r\n@ {DateTime.Now}");
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
                    networkStream.Close();
                }
                finally
                {
                    tcpClient.Close();
                    tcpClient = null!;
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

