using System.Globalization;

namespace MeiOS;

public sealed partial class SimulationModel
{
    public void ExecuteDirective(string text)
    {
        var words = text.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return;
        switch (words[0].ToLowerInvariant())
        {
            case "q": case "quit": Running = false; break;
            case "+": ChangeSpeed(1.1); break;
            case "-": ChangeSpeed(0.9); break;
            case "speed":
                double speed = double.Parse(words[1], CultureInfo.InvariantCulture);
                if (!double.IsFinite(speed) || speed is < 0.1 or > 1000) throw new ArgumentException("Скорость: 0.1..1000");
                Speed = speed; break;
            case "reset": Initialize(); break;
            case "help": Log(HelpText()); break;
            default: throw new ArgumentException("Неизвестная директива. help");
        }
        ValidateInvariants();
    }

    public static string HelpText() => "q | + | - | speed 0.1..1000 | reset | help"
        ;

}
