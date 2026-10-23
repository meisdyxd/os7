using System.Globalization;

namespace MeiOS;

public readonly record struct ParameterRange(int Min, int Max, int Step)
{
    public void Validate()
    {
        if (Min < 0 || Max < Min || Step < 1 || Max > 65535)
            throw new ArgumentException($"Неверный диапазон {Min} {Max} {Step}");
    }
    public int Sample(Random random)
    {
        Validate();
        return Min + random.Next((Max - Min) / Step + 1) * Step;
    }
}
public sealed class ModelConfig
{
    public ParameterRange Memory = new(1536, 2560, 256);
    public ParameterRange TaskSize = new(96, 384, 16);
    public ParameterRange Commands = new(12, 24, 1);
    public ParameterRange IoPercent = new(25, 25, 1);
    public int Slots = 8, Quantum = 4, AgingTicks = 12, SizeBand = 128;
    public int LoadTicks = 1, DispatchTicks = 1, IoInitTicks = 1, IoInterruptTicks = 1, FinishTicks = 1, IoDuration = 4;
    public double Speed = 2;

    public void Validate()
    {
        Memory.Validate(); TaskSize.Validate(); Commands.Validate(); IoPercent.Validate();
        if (Memory.Min < 1 || TaskSize.Min < 1 || Commands.Min < 1 || IoPercent.Max > 100 || Slots is < 1 or > 16 || Quantum < 1 || AgingTicks < 1 || SizeBand < 1 || IoDuration < 1)
            throw new ArgumentException("Недопустимые параметры модели");
        if (new[] { LoadTicks, DispatchTicks, IoInitTicks, IoInterruptTicks, FinishTicks }.Any(x => x < 0))
            throw new ArgumentException("Системные затраты не могут быть отрицательными");
        if (!double.IsFinite(Speed) || Speed is < 0.1 or > 1000) throw new ArgumentException("Скорость: 0.1..1000");
    }
}
