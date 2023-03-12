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

    // Enter the listening loop.
    while (true)
    {
        Console.Write("Waiting for a connection... ");
        TcpClient tcpClient = tcpListener.AcceptTcpClient();
        Console.WriteLine("Connected!");

        new Thread
                (
                    async () =>
                    {
                        try
                        {
                            await threadProcessAsync(tcpClient);
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine($"Caught Exception:\r\n{e}");
                        }
                        finally
                        {
                            tcpClient.Close();
                            tcpClient = null!;
                        }
                    }
                )
                .Start();
    }
}
finally
{
    tcpListener.Stop();
}

async Task threadProcessAsync(TcpClient tcpClient)
{
    var networkStream = tcpClient.GetStream();

    await using var ptyTerminalHost = new PtyTerminalHost<NetworkStream>(networkStream)
    {
        Options = new PtyOptions
        {
            Name = "Custom terminal"
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
    await ptyTerminalHost
                    .StartListenTerminalOutputAsync
                        (
                            async (sender, data) =>
                            {
                                await sender
                                            .Conection
                                            .WriteAsync
                                                    (data);
                                return true;
                            }
                        );

    var p = 0;
    var bytes = new byte[bytesBufferLength];
    var timeoutToken =
                ptyTerminalHost
                            .ListeningOutputCancellationTokenSource
                            .Token;

    while (1 == 1)
    {
        Console.WriteLine($"socket reading ... @ {DateTime.Now}");
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
                b != 0x0D
                //&&
                //b != 0x27
                &&
                !char.IsControl(c)
            )
        {
            Console.WriteLine($"socket writing {c} @ {DateTime.Now}");
            networkStream.WriteByte((byte)'\b');
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

            Console.WriteLine($"Receive Command Line:\r\n{commandLine}");

            if (commandLine == customExitCommandLine)
            {
                await ptyTerminalHost.ExitOnceAsync();
                break;
            }

            await ptyTerminalHost
                                .Terminal!
                                .WriterStream
                                .WriteAsync
                                        (
                                            bytes
                                            , 0
                                            , p
                                            , timeoutToken
                                        );
            await ptyTerminalHost
                                .Terminal!
                                .WriterStream
                                .FlushAsync
                                        (timeoutToken);
            p = 0;
        }
        
    };

    networkStream.Close();
    networkStream = null;


}