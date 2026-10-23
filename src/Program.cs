using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using MeiOS;

Console.InputEncoding = Encoding.UTF8;
Console.OutputEncoding = Encoding.UTF8;
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
if (args.Any(x => x is "/?" or "--help"))
{
    Console.WriteLine("ЛР 7. " + SimulationModel.HelpText());
    Console.WriteLine("dotnet run --project src -- [--ticks N] [--seed N] [--speed V] [--width N] [--height N] [--benchmark seconds] [--self-test] [--json path]");
    return;
}
string? Option(string key)
{
    int index = Array.IndexOf(args, key);
    if (index < 0) return null;
    if (index + 1 == args.Length) throw new ArgumentException($"Нет значения {key}");
    return args[index+1];
}

try
{
    if (args.Contains("--self-test")) { SelfTests.Run(); return; }
    ModelConfig config = new();
    var model = new SimulationModel(config, int.Parse(Option("--seed") ?? "14"));
    model.Initialize();
    int screenWidth = int.Parse(Option("--width") ?? "100");
    int screenHeight = int.Parse(Option("--height") ?? "32");
    if (screenWidth is < 20 or > 240 || screenHeight is < 10 or > 80)
        throw new ArgumentOutOfRangeException("Размер снимка экрана: ширина 20..240, высота 10..80");
    if (Option("--speed") is { } speed) model.ExecuteDirective("speed " + speed);
    double elapsed = 0;
    if (Option("--ticks") is { } count)
    {
        long ticks = long.Parse(count);
        if (ticks < 0) throw new ArgumentException("Число тактов неотрицательное");
        var watch = Stopwatch.StartNew();
        for (long i = 0; i < ticks && model.Running; i++)
        {
            model.ExecuteOneTick();
        }
        elapsed = watch.Elapsed.TotalSeconds;
        Console.WriteLine(string.Join(Environment.NewLine, Dashboard.Build(model, width: screenWidth, height: screenHeight)));
    }
    else
    {
        double? seconds = Option("--benchmark") is { } duration ? double.Parse(duration) : null;
        if (seconds is <= 0) throw new ArgumentException("Длительность замера положительная");
        if (seconds is null && Console.IsInputRedirected) throw new InvalidOperationException("Нужен интерактивный терминал; для пакетного запуска --ticks N");
        elapsed = RealtimeLoop.Run(model, seconds);
    }
    var result = new
    {
        Lab = 7, model.Seed, model.Tick, model.Speed, ElapsedSeconds = elapsed,
        ActualTicksPerSecond = elapsed > 0 ? model.Tick/elapsed : 0,
        model.UsedMemory, model.FreeMemory, model.MemorySize,
        Useful = model.Cpu.UsefulTicks, System = model.Cpu.SystemTicks, Idle = model.Cpu.IdleTicks,
        Completed = model.Completed.Count,
        Residents = model.Residents.Select(p => new { p.Id, p.Size, p.Pc, State=p.State.ToString(), p.BasePriority, p.Priority, p.ReadyTicks, p.IoTicks }).ToArray(),
        model.Throughput, model.RelativePerformance, model.UsefulUtilization,
        CompletedProcesses = model.Completed.Select(p => new { p.Id, Mono=model.MonoTime(p), Multi=model.MultiTime(p), p.ReadyTicks, p.ActiveTicks, p.IoTicks, p.SystemTicks, p.SuspendedTicks }).ToArray(),
        Lines = Dashboard.Build(model, width: screenWidth, height: screenHeight), Events = model.Events.ToArray()
    };
    if (Option("--json") is { } json) File.WriteAllText(json, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented=true }));
    Console.WriteLine($"Итог: тактов={model.Tick}, секунд={elapsed:F3}, фактическая скорость={(elapsed>0?model.Tick/elapsed:0):F3} такт/с");
}
catch (Exception error)
{
    Console.Error.WriteLine($"Ошибка: {error.Message}");
    Environment.ExitCode = 1;
}
