// See https://aka.ms/new-console-template for more information
using Pty.NET;
using System.Net.WebSockets;


const string Data = "abc✓ЖЖЖ①Ⅻㄨㄩ 啊阿鼾齄丂丄狚狛狜狝﨨﨩ˊˋ˙– ⿻〇㐀㐁䶴䶵";

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseWebSockets();
var summaries = new[]
{
    "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
};

app.MapGet("/weatherforecast", () =>
{
    var forecast = Enumerable.Range(1, 5).Select(index =>
        new WeatherForecast
        (
            DateTime.Now.AddDays(index),
            Random.Shared.Next(-20, 55),
            summaries[Random.Shared.Next(summaries.Length)]
        ))
        .ToArray();
    return forecast;
})
.WithName("GetWeatherForecast");

var backSpace = new ArraySegment<byte>(new byte[] { (byte) '\b' });
app.Use(async (context, next) =>
{
    if (context.Request.Path == "/ws")
    {
        if (context.WebSockets.IsWebSocketRequest)
        {
            using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
            
            var ptyTerminalHost = new PtyTerminalHost<WebSocket>(webSocket)
            {
                Options = new PtyOptions
                {
                    Name = "Custom terminal"
                                    ,
                    Cols = Data.Length + Environment.CurrentDirectory.Length + 50
                                    ,
                    Rows = 25
                                    ,
                    Cwd = Environment.CurrentDirectory
                                    ,
                    Environment = new Dictionary<string, string>()
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
                                        await
                                            sender
                                                .Conection
                                                .SendAsync
                                                        (
                                                            data
                                                            , WebSocketMessageType.Binary
                                                            , false
                                                            , sender
                                                                    .ListeningOutputCancellationTokenSource
                                                                    .Token
                                                        );
                                        return
                                            await Task.FromResult(true);
                                    }
                                );

            var p = 0;
            var bytes = new byte[64 * 1024];
            var timeoutToken = ptyTerminalHost.ListeningOutputCancellationTokenSource.Token;

            while (1 == 1)
            {
                Console.WriteLine($"socket reading ... @ {DateTime.Now}");
                ArraySegment<byte> arraySegment = new ArraySegment<byte>(new byte[0]);

                var rr = await webSocket.ReceiveAsync(arraySegment, ptyTerminalHost.ListeningOutputCancellationTokenSource.Token);
                
                if (rr.Count < 0)
                {
                    Thread.Sleep(100);
                    continue;
                }
                byte r = arraySegment.Array![0];
                char c = (char) r;
                if
                    (
                        r != 0x0D
                        //&&
                        //!char.IsSymbol(c)
                        //&&
                        //!char.IsControl(c)
                    )
                {
                    Console.WriteLine($"socket writing {(char) r} ... @ {DateTime.Now}");
                    await webSocket.SendAsync(arraySegment.Array!, WebSocketMessageType.Binary, false, ptyTerminalHost.ListeningOutputCancellationTokenSource.Token);
                    await webSocket.SendAsync(backSpace, WebSocketMessageType.Binary, false, ptyTerminalHost.ListeningOutputCancellationTokenSource.Token);
                    await webSocket.SendAsync(arraySegment.Array!, WebSocketMessageType.Binary, false, ptyTerminalHost.ListeningOutputCancellationTokenSource.Token);
                }

                var buffer = arraySegment.Array!;
                var l = buffer.Length;
                Buffer.BlockCopy(buffer, 0, bytes, p, l);
                p += l;

                if
                    (
                        r == 0x0D   // enter
                        ||
                        r == 0x26   // up
                        ||
                        r == 0x28   // down
                    )
                {
                    await ptyTerminalHost.Terminal!.WriterStream.WriteAsync(bytes, 0, p, timeoutToken);
                    await ptyTerminalHost.Terminal!.WriterStream.FlushAsync(timeoutToken);
                    p = 0;
                }
            }
        }
        else
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
        }
    }
    else
    {
        await next(context);
    }

});

app.Run();

internal record WeatherForecast(DateTime Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}