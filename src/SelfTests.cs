using System.Text.Json;

namespace MeiOS;

public static class SelfTests
{
    private static readonly List<object> Results = [];
    private static void Check(string name, bool condition, string actual)
    {
        Results.Add(new { name, passed=condition, actual });
        if (!condition) throw new InvalidOperationException("Тест не пройден: " + name + " / " + actual);
    }
    public static SimulationModel Empty(int quantum=4, int duration=3)
    {
        var config=new ModelConfig { Slots=8, Memory=new(512,512,1), TaskSize=new(64,64,1), Quantum=quantum, AgingTicks=3, LoadTicks=0, DispatchTicks=0, IoInitTicks=0, IoInterruptTicks=0, FinishTicks=0, IoDuration=duration };
        var m=new SimulationModel(config);
        m.Initialize();
        m.AutoLoad=false;
        Array.Clear(m.Table);
        foreach(var q in m.ReadyQueues) q.Clear();
        m.Cpu.Active=null; m.SystemJobs.Clear(); m.Io.Clear();
        return m;
    }
    public static Psw Insert(SimulationModel m, int size, Command[]? commands=null)
    {
        m.SetPendingTask(size,commands);
        if(!m.LoadNewTask()) throw new InvalidOperationException("Ошибка тестовой загрузки");
        return m.Residents.Last();
    }
    public static void Run()
    {
        // Интерфейс: узкие окна, все экраны, длинные строки и границы прокрутки.
        var ui = Empty();
        Insert(ui, 64); Insert(ui, 200);
        ui.Log(new string('Ж', 500));
        foreach (var process in ui.Residents) { process.Id = 12345678 + process.Slot; process.Pc = int.MaxValue; }
        ui.Cpu.Counter = int.MaxValue;
        bool layoutOk = true;
        foreach (int width in new[] { 1, 40, 79, 119, 179 })
        foreach (int height in new[] { 5, 10, 24, 40 })
        for (int view = 0; view < 3; view++)
        {
            int actualScroll = -1;
            var frame = Dashboard.Build(ui, "task " + new string('9', 100), width, height, view, int.MaxValue, true, value => actualScroll = value);
            layoutOk &= frame.Count == height && frame.All(line => line.Length <= width) && actualScroll >= 0 && actualScroll < int.MaxValue;
            if (width >= 5) layoutOk &= frame[^1].EndsWith('9');
            frame = Dashboard.Build(ui, "", width, height, view, -100);
            layoutOk &= frame.Count == height && frame.All(line => line.Length <= width);
        }
        Check("dashboard_layout", layoutOk, "all views, 20 window sizes, long IDs/log/input, scroll limits");
        var fullScreen = Empty();
        for (int i = 0; i < 100; i++) fullScreen.ExecuteOneTick();
        for (int i = 0; i < 8; i++) Insert(fullScreen, 64);
        fullScreen.Cpu.UsefulTicks = 40; fullScreen.Cpu.SystemTicks = 20; fullScreen.Cpu.IdleTicks = 40;
        fullScreen.Completed.Add(new Psw
        {
            Id = 99, Size = 128, Program = [new(Op.Compute), new(Op.Exit)], IoCount = 0,
            State = ProcessState.Completed, Arrival = 0, End = 2, ActiveTicks = 2
        });
        var completeFrame = Dashboard.Build(fullScreen, width: 180, height: 48);
        Check("dashboard_metrics_full_screen", !completeFrame.Any(line => line.StartsWith("Строки "))
            && completeFrame.Any(line => line.Contains("Пропускная способность"))
            && completeFrame.Any(line => line.Contains("полезная загрузка"))
            && completeFrame.Any(line => line.Contains("Средние входные"))
            && completeFrame.Any(line => line.Contains("Средние выходные")),
            $"180x48, relative={fullScreen.RelativePerformance:F2}%, utilization={fullScreen.UsefulUtilization:F1}%");
        var m=Empty();
        Insert(m,200); Insert(m,300);
        m.SetPendingTask(20);
        Check("memory_reject",!m.LoadNewTask() && m.FreeMemory==12,$"free={m.FreeMemory}");
        m=Empty();
        for(int i=0;i<8;i++) Insert(m,1);
        m.SetPendingTask(1);
        Check("psw_capacity",!m.LoadNewTask() && m.FreeMemory==504,$"slots={m.Residents.Count()}");
        m=Empty(); var p=Insert(m,64);
        m.Dispatch(); m.ExecuteOneTick();
        Check("hardware_pc",m.Cpu.Counter==1 && p.Pc==1,$"pc={p.Pc}");
        m.ExecuteDirective("speed 1000"); m.ChangeSpeed(1.1);
        Check("speed_max",m.Speed==1000,$"speed={m.Speed}");
        m.ExecuteDirective("speed 0.1"); m.ChangeSpeed(0.9);
        Check("speed_min",m.Speed==0.1,$"speed={m.Speed}");
        m.ExecuteDirective("speed 100"); m.ExecuteDirective("+");
        Check("speed_plus",Math.Abs(m.Speed-110)<1e-9,$"speed={m.Speed}");
        m.ExecuteDirective("speed 100"); m.ExecuteDirective("-");
        Check("speed_minus",Math.Abs(m.Speed-90)<1e-9,$"speed={m.Speed}");
        var a=new SimulationModel(); var b=new SimulationModel(); a.Initialize(); b.Initialize();
        Check("seed_repeat",string.Join('|',a.Residents)==string.Join('|',b.Residents),string.Join('|',a.Residents));
        var computeOnly=Enumerable.Repeat(new Command(Op.Compute),30).Append(new Command(Op.Exit)).ToArray();
        m=Empty(2); var first=Insert(m,64,computeOnly); var second=Insert(m,64,computeOnly); m.Dispatch();
        m.ExecuteOneTick(); m.ExecuteOneTick();
        Check("rr_equal_priority",m.Cpu.Active==second,$"active={m.Cpu.Active?.Id}");
        m=Empty(20); first=Insert(m,64,computeOnly); m.Dispatch(); second=Insert(m,300,computeOnly);
        m.ExecuteOneTick();
        Check("relative_no_preemption",m.Cpu.Active==first,$"active={m.Cpu.Active?.Id}, new_priority={second.Priority}");
        m=Empty(); first=Insert(m,64); second=Insert(m,300);
        Check("large_jobs_priority",m.GetNextProcess()==second,$"base small={first.BasePriority}, large={second.BasePriority}");
        for(int i=0;i<6;i++) m.AgeReadyProcesses();
        Check("aging",first.Priority==2,$"priority={first.Priority}");
        m=Empty(100); p=Insert(m,64,[new(Op.Compute),new(Op.Exit)]);
        m.ExecuteOneTick(); m.ExecuteOneTick();
        Check("compute_exit_memory",p.Memory[2]==5 && m.Completed.Count==1 && m.UsedMemory==0,$"result={p.Memory[2]}, completed={m.Completed.Count}, used={m.UsedMemory}");
        m=Empty(100); p=Insert(m,64,[new(Op.Compute,99),new(Op.Exit)]);
        m.ExecuteOneTick();
        Check("memory_protection",p.State==ProcessState.Killed && m.UsedMemory==0,$"state={p.State}");
        m=Empty(100,3); p=Insert(m,64,[new(Op.Io),new(Op.Exit)]);
        m.ExecuteOneTick();
        Check("io_block",p.State==ProcessState.BlockedIo && m.Io.Single().Left==3,$"state={p.State}, IO_left={m.Io.Single().Left}");
        for(int i=0;i<4;i++) m.ExecuteOneTick();
        Check("io_interrupt_completion",p.State==ProcessState.Completed && p.End==5,$"state={p.State}, end={p.End}");
        var screen = Dashboard.Build(m, width: 79, height: 24);
        Check("stable_dashboard",screen.Count == 24 && screen.All(line => line.Length <= 79) && screen[^1].Contains("Q выход"), $"lines={screen.Count}, controls fixed");
        var noIoModel=Empty(100); var noIoProcess=Insert(noIoModel,64,[new(Op.Compute),new(Op.Exit)]);
        noIoModel.ExecuteOneTick(); noIoModel.ExecuteOneTick();
        Check("mono_multi_no_io",noIoModel.Completed.Count == 1 && noIoModel.MonoTime(noIoProcess) == 2 && noIoModel.MultiTime(noIoProcess) == 2,
            $"commands=2, IO=0, expected=2/2, actual={noIoModel.MonoTime(noIoProcess)}/{noIoModel.MultiTime(noIoProcess)}");
        Check("mono_multi_one_io",m.MonoTime(p)==5 && m.MultiTime(p)==5,$"mono={m.MonoTime(p)}, multi={m.MultiTime(p)}");
        Check("residence_decomposition",p.ReadyTicks+p.ActiveTicks+p.IoTicks+p.SystemTicks+p.SuspendedTicks==m.MultiTime(p),$"R/A/IO/S={p.ReadyTicks}/{p.ActiveTicks}/{p.IoTicks}/{p.SystemTicks}");
        m=Empty(1); first=Insert(m,64,[new(Op.Compute),new(Op.Exit)]); second=Insert(m,64,[new(Op.Compute),new(Op.Exit)]);
        for(int i=0;i<4;i++) m.ExecuteOneTick();
        Check("multi_two_jobs",m.Completed.Count==2 && m.MultiTime(first)==3 && m.MultiTime(second)==4 && m.RelativePerformance==100,$"multi={m.MultiTime(first)}/{m.MultiTime(second)}, P={m.RelativePerformance}");
        m=Empty(100,3); p=Insert(m,64,[new(Op.Io),new(Op.Io),new(Op.Exit)]);
        for(int i=0;i<9;i++) m.ExecuteOneTick();
        Check("two_io",m.MonoTime(p)==9 && m.MultiTime(p)==9,$"mono={m.MonoTime(p)}, multi={m.MultiTime(p)}");
        m=Empty(100); m.Config.LoadTicks=1; m.Config.DispatchTicks=1; m.Config.FinishTicks=1;
        p=Insert(m,64,[new(Op.Compute),new(Op.Exit)]);
        for(int i=0;i<5;i++) m.ExecuteOneTick();
        Check("system_costs",m.MonoTime(p)==5 && m.MultiTime(p)==5 && m.Cpu.SystemTicks==3,$"mono={m.MonoTime(p)}, multi={m.MultiTime(p)}, sys={m.Cpu.SystemTicks}");
        Console.WriteLine(JsonSerializer.Serialize(Results,new JsonSerializerOptions{WriteIndented=true}));
    }
}
