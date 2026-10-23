using System.Globalization;

namespace MeiOS;

public enum ProcessState { Absent, Loading, Ready, Active, InitializingIo, BlockedIo, EndingIo, Finishing, Suspended, Completed, Killed, BlockedMemory }
public sealed class Psw
{
    public int Slot, Id, Size, Pc, Quantum, BasePriority, Priority, ReadyAge;
    public long ReadyOrder, Arrival, End, ReadyTicks, ActiveTicks, IoTicks, SystemTicks, SuspendedTicks;
    public ProcessState State;
    public Command[] Program = [];
    public int[] Memory = [2, 3, 0];
    public int IoCount;
    public override string ToString() => $"{Id}:{Size}:{Pc}:{State}:{Priority}:{Quantum}";
}
public sealed class Cpu
{
    public Psw? Active;
    public int Counter;
    public string LastCommand = "-";
    public long UsefulTicks, SystemTicks, IdleTicks;
    public readonly Queue<int> History = new();
}
public enum Op { Compute, Io, Exit }
public readonly record struct Command(Op Code, int A = 0, int B = 1, int Target = 2);
public sealed record IoJob(Psw Owner, int Remaining)
{
    public int Left = Remaining;
}
public sealed class SystemJob(string name, Psw owner, int ticks, Action done)
{
    public string Name = name;
    public Psw Owner = owner;
    public int Left = ticks;
    public Action Done = done;
}

/// <summary>Единственный владелец данных модели. Реального параллелизма нет: один такт атомарен.</summary>
public sealed partial class SimulationModel
{
    public readonly ModelConfig Config;
    public readonly Cpu Cpu = new();
    public readonly Psw?[] Table;
    public readonly List<Psw> Completed = [];
    public readonly List<IoJob> Io = [];
    public readonly List<SystemJob> SystemJobs = [];
    public readonly Queue<string> Events = new();
    public readonly List<Psw>[] ReadyQueues = [[], [], []];
    private Random random;
    private int nextId;
    private long order;
    public Psw? NewTask { get; private set; }
    public int MemorySize { get; private set; }
    public int UsedMemory => Table.OfType<Psw>().Sum(p => p.Size);
    public int FreeMemory => MemorySize - UsedMemory;
    public long Tick { get; private set; }
    public double Speed { get; private set; } = 2;
    public bool AutoLoad { get; set; } = true;
    public bool StopWhenEmpty { get; set; }
    public bool Running { get; set; } = true;
    public int Seed { get; private set; }
    public IEnumerable<Psw> Residents => Table.OfType<Psw>();

    public SimulationModel(ModelConfig? config = null, int seed = 14)
    {
        Config = config ?? new ModelConfig();
        Config.Validate();
        Seed = seed;
        random = new Random(seed);
        Table = new Psw?[Config.Slots];
    }

    public void Initialize()
    {
        random = new Random(Seed);
        Array.Clear(Table);
        Completed.Clear(); Io.Clear(); SystemJobs.Clear(); Events.Clear();
        foreach (var queue in ReadyQueues) queue.Clear();
        Cpu.Active = null; Cpu.Counter = 0; Cpu.LastCommand = "-";
        Cpu.UsefulTicks = Cpu.SystemTicks = Cpu.IdleTicks = 0;
        Cpu.History.Clear(); Tick = 0; nextId = 1; order = 0;
        Running = true; AutoLoad = true; StopWhenEmpty = false;
        Speed = Config.Speed;
        MemorySize = Config.Memory.Sample(random);
        GenerateNewTask();
        LoadInitialTasks();
        CheckSystemState();
    }

    public void GenerateNewTask()
    {
        NewTask = CreateTask(nextId++, Config.TaskSize.Sample(random));
    }

    public Psw CreateTask(int id, int size, Command[]? program = null)
    {
        var p = new Psw { Id = id, Size = size, Slot = -1, State = ProcessState.Absent };
        p.BasePriority = Math.Clamp((size - 1) / Config.SizeBand, 0, 2);
        p.Priority = p.BasePriority;
        p.Program = program ?? GenerateCommands(Config.Commands.Sample(random), Config.IoPercent.Sample(random));
        p.IoCount = p.Program.Count(c => c.Code == Op.Io);
        return p;
    }

    public bool HasAvailableMemory() => NewTask is not null && NewTask.Size <= FreeMemory;

    public bool LoadNewTask()
    {
        if (!HasAvailableMemory()) return false;
        int slot = Array.FindIndex(Table, p => p is null);
        if (slot < 0) return false;
        var p = NewTask!;
        p.Slot = slot; p.Arrival = Tick; Table[slot] = p;
        NewTask = null;
        Process(p, ProcessState.Loading, "загрузка");
        AddSystemJob("LOAD", p, Config.LoadTicks, () => Process(p, ProcessState.Ready, "загружен"));
        GenerateNewTask();
        return true;
    }

    public void LoadInitialTasks()
    {
        while (LoadNewTask()) { }
    }

    public void ExecuteOneTick()
    {
        if (!Running) return;
        CheckSystemState();
        foreach (var p in Residents)
        {
            if (p.State == ProcessState.Active && SystemJobs.Count == 0) p.ActiveTicks++;
            else if (p.State == ProcessState.Active) p.SystemTicks++;
            else if (p.State == ProcessState.BlockedIo) p.IoTicks++;
            else if (p.State == ProcessState.Suspended) p.SuspendedTicks++;
            else if (p.State != ProcessState.Ready) p.SystemTicks++;
        }
        IncreaseReadyTime(1);
        // Снимок IO до выполнения команды: новое IO начнёт отсчёт в следующем такте.
        var ioAtStart = Io.ToArray();
        Tick++;
        if (SystemJobs.Count > 0) ExecuteSystemTick();
        else if (Cpu.Active is not null) ExecuteCommand();
        else Cpu.IdleTicks++;
        StepIo(ioAtStart);
        AgeReadyProcesses();
        CheckSystemState();
        ValidateInvariants();
    }

    public void CheckSystemState()
    {
        // Новые задания допускаются при освобождении CPU: относительные приоритеты.
        if (Cpu.Active is null)
        {
            if (AutoLoad) LoadInitialTasks();
            if (SystemJobs.Count == 0) Dispatch();
        }
        if (StopWhenEmpty && !Residents.Any() && SystemJobs.Count == 0)
            Running = false;
    }

    public void ChangeSpeed(double multiplier)
    {
        Speed = Math.Clamp(Speed * multiplier, 0.1, 1000);
    }

    public void Process(Psw p, ProcessState state, string reason)
    {
        // Регулировщик: сохраняет CPU, удаляет старые связи, создаёт новые.
        if (Cpu.Active == p)
        {
            SaveState(p);
            Cpu.Active = null;
        }
        foreach (var queue in ReadyQueues) queue.Remove(p);
        var previous = p.State;
        p.State = state;
        if (state == ProcessState.Ready)
        {
            p.ReadyAge = 0; p.Priority = p.BasePriority; p.ReadyOrder = order++;
            ReadyQueues[p.Priority].Add(p);
        }
        Log($"P{p.Id}: {previous} -> {state} ({reason})");
    }

    public void SaveState(Psw p) => p.Pc = Cpu.Counter;

    public void RestoreState(Psw p)
    {
        Cpu.Counter = p.Pc; Cpu.Active = p;
        p.Quantum = Config.Quantum;
    }

    public Psw? GetNextProcess()
    {
        for (int priority = 2; priority >= 0; priority--)
            if (ReadyQueues[priority].Count > 0) return ReadyQueues[priority][0];
        return null;
    }

    public void Dispatch()
    {
        var p = GetNextProcess();
        if (p is null) { Cpu.Active = null; return; }
        Process(p, ProcessState.Active, "диспетчеризация");
        RestoreState(p);
        Cpu.History.Enqueue(p.Id);
        while (Cpu.History.Count > 16) Cpu.History.Dequeue();
        AddSystemJob("DISPATCH", p, Config.DispatchTicks, () => { });
    }

    public void AgeReadyProcesses()
    {
        // Стабильная перестройка очередей сохраняет FIFO между равноприоритетными.
        var ready = ReadyQueues.SelectMany(q => q).OrderBy(p => p.ReadyOrder).ToArray();
        foreach (var queue in ReadyQueues) queue.Clear();
        foreach (var p in ready)
        {
            p.ReadyAge++;
            p.Priority = Math.Min(2, p.BasePriority + p.ReadyAge / Config.AgingTicks);
            ReadyQueues[p.Priority].Add(p);
        }
    }

    public void ValidateInvariants()
    {
        if (UsedMemory > MemorySize || FreeMemory < 0) throw new InvalidOperationException("Переполнение памяти");
        var ready = ReadyQueues.SelectMany(q => q).ToArray();
        if (ready.Distinct().Count() != ready.Length) throw new InvalidOperationException("Дубли в очередях");
        if (ready.Any(p => p.State != ProcessState.Ready) || ready.Length != Residents.Count(p => p.State == ProcessState.Ready))
            throw new InvalidOperationException("Нарушено соответствие PSW и очередей");
        if (Residents.Count(p => p.State == ProcessState.Active) != (Cpu.Active is null ? 0 : 1))
            throw new InvalidOperationException("Несогласованное состояние CPU");
    }

    public void Log(string message)
    {
        Events.Enqueue($"{Tick}: {message}");
        while (Events.Count > 6) Events.Dequeue();
    }

    public void SetPendingTask(int size, Command[]? program = null)
    {
        if (size <= 0) throw new ArgumentException("Размер должен быть положительным");
        NewTask = CreateTask(nextId++, size, program);
    }
}
