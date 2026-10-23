namespace MeiOS;

public sealed partial class SimulationModel
{
    public Command[] GenerateCommands(int count, int ioPercent)
    {
        var commands = new Command[count + 1];
        for (int i = 0; i < count; i++)
            commands[i] = new Command(random.Next(100) < ioPercent ? Op.Io : Op.Compute);
        commands[count] = new Command(Op.Exit);
        return commands;
    }

    public Command FetchCommand(Psw p)
    {
        if (Cpu.Counter < 0 || Cpu.Counter >= p.Program.Length)
            throw new InvalidOperationException("Счётчик команд вне программы");
        return p.Program[Cpu.Counter];
    }

    public Op DecodeCommand(Command command) => command.Code;

    public int ReadOperand(Psw p, int address)
    {
        if (address < 0 || address >= p.Memory.Length) throw new InvalidOperationException("Нарушение защиты памяти");
        return p.Memory[address];
    }

    public void WriteResult(Psw p, int address, int value)
    {
        if (address < 0 || address >= p.Memory.Length) throw new InvalidOperationException("Нарушение защиты памяти");
        p.Memory[address] = value;
    }

    public void ExecuteCommand()
    {
        var p = Cpu.Active!;
        try
        {
            var command = FetchCommand(p);
            Cpu.Counter++; p.Pc = Cpu.Counter; p.Quantum--; Cpu.UsefulTicks++;
            Cpu.LastCommand = DecodeCommand(command).ToString();
            DoOperation(p, command);
            if (Cpu.Active == p && p.Quantum <= 0) Process(p, ProcessState.Ready, "таймер RR");
        }
        catch (InvalidOperationException error)
        {
            Log(error.Message);
            Terminate(p, false);
        }
    }

    public void DoOperation(Psw p, Command command)
    {
        switch (DecodeCommand(command))
        {
            case Op.Compute:
                WriteResult(p, command.Target, ReadOperand(p, command.A) + ReadOperand(p, command.B));
                break;
            case Op.Io: InitializeIo(p); break;
            case Op.Exit: FinishProcess(p); break;
            default: throw new InvalidOperationException("Неизвестная команда");
        }
    }

    public void InitializeIo(Psw p)
    {
        Process(p, ProcessState.InitializingIo, "команда IO");
        AddSystemJob("IO_INIT", p, Config.IoInitTicks, () =>
        {
            Process(p, ProcessState.BlockedIo, "устройство запущено");
            Io.Add(new IoJob(p, Config.IoDuration));
        });
    }

    public void StepIo(IEnumerable<IoJob> jobs)
    {
        foreach (var job in jobs)
        {
            if (--job.Left > 0) continue;
            Io.Remove(job);
            Process(job.Owner, ProcessState.EndingIo, "прерывание IO");
            AddSystemJob("IO_INTERRUPT", job.Owner, Config.IoInterruptTicks,
                () => Process(job.Owner, ProcessState.Ready, "конец IO"));
        }
    }

    public void FinishProcess(Psw p)
    {
        Process(p, ProcessState.Finishing, "EXIT");
        AddSystemJob("FINISH", p, Config.FinishTicks, () => Terminate(p, true));
    }

    public void Terminate(Psw p, bool completed)
    {
        Process(p, completed ? ProcessState.Completed : ProcessState.Killed, "выгрузка");
        Io.RemoveAll(job => job.Owner == p);
        SystemJobs.RemoveAll(job => job.Owner == p);
        Table[p.Slot] = null; p.End = Tick;
        if (completed) Completed.Add(p);
    }

    public void AddSystemJob(string name, Psw p, int ticks, Action done)
    {
        if (ticks == 0) { done(); return; }
        SystemJobs.Add(new SystemJob(name, p, ticks, done));
    }

    public void ExecuteSystemTick()
    {
        var job = SystemJobs[0];
        Cpu.SystemTicks++;
        Cpu.LastCommand = job.Name;
        if (--job.Left == 0)
        {
            SystemJobs.RemoveAt(0);
            job.Done();
        }
    }
}
