using System.Diagnostics;

namespace MeiOS;

public static class RealtimeLoop
{
    public static double Run(SimulationModel model, double? benchmarkSeconds = null)
    {
        var watch = Stopwatch.StartNew();
        double previous = 0, credit = 0, nextDraw = 0;
        string input = "";
        bool commandMode = false;
        int view = 0, scroll = 0;
        bool terminal = !Console.IsOutputRedirected;
        if (terminal) { Console.Clear(); Console.CursorVisible = false; }
        try
        {
            while (model.Running && (benchmarkSeconds is null || watch.Elapsed.TotalSeconds < benchmarkSeconds))
            {
                double now = watch.Elapsed.TotalSeconds;
                // Скорость в прошедшем интервале: изменение влияет только на будущие такты.
                credit += (now-previous)*model.Speed;
                previous = now;
                while (credit >= 1 && model.Running) { model.ExecuteOneTick(); credit--; }
                if (!Console.IsInputRedirected)
                {
                    while (Console.KeyAvailable)
                    {
                        var key = Console.ReadKey(true);
                        if (commandMode)
                        {
                            if (key.Key == ConsoleKey.Enter)
                            {
                                try { model.ExecuteDirective(input); }
                                catch (Exception e) when (e is ArgumentException or FormatException or IndexOutOfRangeException or InvalidOperationException or OverflowException) { model.Log(e.Message); }
                                input = ""; commandMode = false;
                            }
                            else if (key.Key == ConsoleKey.Escape) { input=""; commandMode=false; }
                            else if (key.Key == ConsoleKey.Backspace) { if (input.Length>0) input=input[..^1]; }
                            else if (!char.IsControl(key.KeyChar) && input.Length < 100) input += key.KeyChar;
                        }
                        else
                        {
                            if (key.Key is ConsoleKey.F1 or ConsoleKey.F2 or ConsoleKey.F3 or ConsoleKey.H)
                            {
                                view = key.Key == ConsoleKey.F1 ? 0 : key.Key == ConsoleKey.F2 ? 1 : 2;
                                scroll = 0;
                            }
                            else if (key.Key == ConsoleKey.PageUp) scroll = Math.Max(0, scroll - Math.Max(1, Console.WindowHeight - 6));
                            else if (key.Key == ConsoleKey.PageDown) scroll = (int)Math.Min(int.MaxValue, (long)scroll + Math.Max(1, Console.WindowHeight - 6));
                            else if (key.KeyChar == ':') commandMode=true;
                            else if (key.KeyChar is '+' or '-') model.ExecuteDirective(key.KeyChar.ToString());
                            else if (key.Key == ConsoleKey.Q) model.ExecuteDirective("q");
                            else if (key.Key == ConsoleKey.R) { model.ExecuteDirective("reset"); credit=0; }
                        }
                    }
                }
                if (now >= nextDraw)
                {
                    if (terminal)
                    {
                        try
                        {
                            int width = Math.Max(1, Console.WindowWidth - 1);
                            int height = Math.Max(5, Console.WindowHeight);
                            int top = Console.WindowTop, left = Console.WindowLeft;
                            var lines = Dashboard.Build(model, input, width, height, view, scroll, commandMode, actual => scroll = actual);
                            Console.CursorVisible = false;
                            // Без WriteLine: последний ряд не прокручивает буфер; пустые строки стирают старый кадр.
                            for (int row = 0; row < lines.Count; row++)
                            {
                                Console.SetCursorPosition(left, top + row);
                                Console.Write(lines[row].PadRight(width));
                            }
                            if (commandMode)
                            {
                                Console.SetCursorPosition(left + Math.Min(lines[^1].Length, width - 1), top + height - 1);
                                Console.CursorVisible = true;
                            }
                        }
                        catch (ArgumentOutOfRangeException) { /* Размер окна изменился во время кадра; повторим следующий. */ }
                        catch (IOException) { /* Терминал временно недоступен. */ }
                    }
                    nextDraw = now+0.1;
                }
                // Короткий сон разгружает CPU; кредит компенсирует погрешность таймера ОС.
                Thread.Sleep(1);
            }
            return watch.Elapsed.TotalSeconds;
        }
        finally
        {
            if (terminal) { Console.CursorVisible=true; Console.WriteLine(); }
        }
    }
}
