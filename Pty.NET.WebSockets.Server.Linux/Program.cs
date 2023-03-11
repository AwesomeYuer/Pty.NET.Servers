

// See https://aka.ms/new-console-template for more information
using Pty.Net;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Text;
using System.Diagnostics;
using System.Net.Sockets;
using System.Net;

Console.WriteLine("Hello, World!");


const uint CtrlCExitCode = 0xC000013A;

int TestTimeoutMs = 300_0000;
//Debugger.IsAttached ? 300_0000 : 5_000;

CancellationToken TimeoutToken = new CancellationTokenSource(TestTimeoutMs).Token;

var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
const string Data = "abc✓ЖЖЖ①Ⅻㄨㄩ 啊阿鼾齄丂丄狚狛狜狝﨨﨩ˊˋ˙– ⿻〇㐀㐁䶴䶵";

string host = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? Path.Combine(Environment.SystemDirectory, "cmd.exe") : "sh";
var options = new PtyOptions
{
    Name = "Custom terminal"
                            ,
    Cols = Data.Length + Environment.CurrentDirectory.Length + 50
                            ,
    Rows = 25
                            ,
    Cwd = Environment.CurrentDirectory
                            ,
    App = host
                            ,
    Environment = new Dictionary<string, string>()
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
                                    await networkStream.WriteAsync(buffer, 0, count);
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

                                Console.WriteLine(output);

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


app.Use(async (context, next) =>
{
    if (context.Request.Path == "/ws")
    {
        if (context.WebSockets.IsWebSocketRequest)
        {
            using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
            //await Echo(webSocket);
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