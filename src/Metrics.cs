namespace MeiOS;

public sealed partial class SimulationModel
{
    public void IncreaseReadyTime(int ticks)
    {
        foreach (var p in Residents.Where(p => p.State == ProcessState.Ready)) p.ReadyTicks += ticks;
    }

    public long MonoTime(Psw p) => Config.LoadTicks + p.Program.Length + Config.FinishTicks
        + (long)p.IoCount * (Config.IoDuration + Config.IoInitTicks + Config.IoInterruptTicks)
        + (long)(p.IoCount + 1) * Config.DispatchTicks;

    public long MultiTime(Psw p) => p.End - p.Arrival;

    public double Throughput => Tick == 0 ? 0 : (double)Completed.Count / Tick;

    public double RelativePerformance => Tick == 0 ? 0 : 100.0 * Completed.Sum(MonoTime) / Tick;

    public double UsefulUtilization => Tick == 0 ? 0 : 100.0 * Cpu.UsefulTicks / Tick;
}
