using System.Globalization;
using System.Text.RegularExpressions;

namespace MeiOS;

public static class Dashboard
{
    private static readonly int Lab = 7;
    public static string StateName(ProcessState state) => state switch
    {
        ProcessState.Absent => "Отсутствует", ProcessState.Loading => "Загрузка",
        ProcessState.Ready => "Готов", ProcessState.Active => "Активен",
        ProcessState.InitializingIo => "Запуск IO", ProcessState.BlockedIo => "Ожидает IO",
        ProcessState.EndingIo => "Конец IO", ProcessState.Finishing => "Выгрузка",
        ProcessState.Suspended => "Приостановлен", ProcessState.Completed => "Завершён",
        ProcessState.Killed => "Удалён", _ => "Ждёт память"
    };

    // Фиксированный кадр: данные прокручиваются, управление всегда видно.
    public static List<string> Build(SimulationModel m, string input = "", int width = 100,
        int height = 32, int view = 0, int scroll = 0, bool commandMode = false, Action<int>? onScroll = null)
    {
        width = Math.Max(1, width); height = Math.Max(5, height);
        var content = new List<string>();
        if (view == 2) DrawHelp(content);
        else if (view == 1) DrawDetails(m, content);
        else
        {
            DrawSystem(m, content); DrawNewTask(m, content); DrawCpu(m, content);
            DrawProcesses(m, content, width); DrawHistory(m, content); DrawMeans(m, content);
            content.Add("Событие: " + EventText(m.Events.LastOrDefault() ?? "нет"));
        }
        var wrapped = content.SelectMany(line => Wrap(line, width)).ToList();
        int capacity = height - 4, start = Math.Clamp(scroll, 0, Math.Max(0, wrapped.Count - capacity));
        onScroll?.Invoke(start);
        var heading = new List<string>(); DrawStatic(heading);
        var frame = new List<string> { Fit(heading[0] + " | " + (view == 1 ? "ДЕТАЛИ" : view == 2 ? "ПОМОЩЬ" : "МОДЕЛЬ"), width) };
        frame.AddRange(wrapped.Skip(start).Take(capacity));
        while (frame.Count < height - 3) frame.Add("");
        frame.Add(Fit(wrapped.Count > capacity
            ? $"Строки {start + 1}-{Math.Min(start + capacity, wrapped.Count)} из {wrapped.Count}. PgUp/PgDn: прокрутка"
            : "PC: счётчик команд | Б/П: базовый/текущий приоритет | IO: ввод/вывод", width));
        frame.Add(Fit("F1 модель | F2 детали | F3/H помощь | PgUp/PgDn прокрутка", width));
        string prompt = "> " + input;
        if (prompt.Length > width) prompt = width >= 5 ? "> ..." + input[^Math.Max(0, width - 5)..] : prompt[^width..];
        frame.Add(commandMode ? prompt : Fit("+/- скорость | R сброс | : команда + Enter | Q выход", width));
        return frame;
    }
    public static void DrawStatic(List<string> lines) => lines.Add($"meiOS | ЛР {Lab} | Вариант 14");
    public static void DrawSystem(SimulationModel m, List<string> lines)
    {
        lines.Add($"Такт: {m.Tick} | Скорость: {m.Speed:F1} такт/с | Завершено: {m.Completed.Count}");
        lines.Add($"Память: занято {m.UsedMemory}, свободно {m.FreeMemory}, всего {m.MemorySize} | Процессы: {m.Residents.Count()}/{m.Table.Length}");
        if (Lab < 3) return;
        lines.Add("Готовы: " + string.Join(" | ", Enumerable.Range(0, 3).Reverse()
            .Select(i => $"Q{i} ({m.ReadyQueues[i].Count}) [{Ids(m.ReadyQueues[i].Select(p => p.Id))}]")));
        int next = Enumerable.Range(0, 3).Reverse().FirstOrDefault(i => m.ReadyQueues[i].Count > 0, -1);
        lines.Add(next < 0 ? "Выбор CPU: готовых процессов нет; все очереди пустые"
            : $"Следующий: P{m.ReadyQueues[next][0].Id} из Q{next} (высшая непустая очередь)");
        lines.Add($"Пропускная способность: {m.Throughput:F4} заданий/такт; относительная производительность {m.RelativePerformance:F2}%");
    }
    public static void DrawNewTask(SimulationModel m, List<string> lines)
    {
        var p = m.NewTask;
        lines.Add(p is null ? "Новое задание: нет" : $"Новое: P{p.Id}, память {p.Size}"
            + (Lab >= 4 ? $", команд {p.Program.Length}, IO {p.IoCount}" : "")
            + (Lab >= 9 ? $" | Автозагрузка: {(m.AutoLoad ? "вкл" : "выкл")}" : ""));
    }
    public static void DrawCpu(SimulationModel m, List<string> lines)
    {
        lines.Add($"CPU: {(m.Cpu.Active is not null ? "P" + m.Cpu.Active.Id : m.SystemJobs.Count > 0 ? "работа ОС" : "ожидание")} | PC: {m.Cpu.Counter}"
            + (Lab >= 3 ? $" | Квант: {m.Cpu.Active?.Quantum.ToString() ?? "-"}/{m.Config.Quantum}" : "")
            + (Lab >= 4 ? $" | Последняя: {m.Cpu.LastCommand}" : ""));
        if (Lab < 4) return;
        string io = string.Join(", ", m.Io.Select(j => $"P{j.Owner.Id}:{j.Left}"));
        var job = m.SystemJobs.FirstOrDefault();
        lines.Add($"IO (осталось тактов): {(io.Length == 0 ? "нет" : io)} | ОС: {(job is null ? "нет" : job.Name + ":P" + job.Owner.Id + ":" + job.Left)}");
        lines.Add($"Такты CPU: полезные {m.Cpu.UsefulTicks}, ОС {m.Cpu.SystemTicks}, простой {m.Cpu.IdleTicks} | полезная загрузка {m.UsefulUtilization:F1}%");
    }
    public static void DrawProcesses(SimulationModel m, List<string> lines, int width = 100)
    {
        bool narrow = width < 68;
        int[] sizes = narrow ? [2, 6, 6, 13, 3] : Lab >= 3 ? [2, 7, 6, 11, 14, 5, 5, 7] : [2, 7, 6, 11, 14];
        string[] headings = narrow ? ["№", "ID", "PC", "Состояние", "П"] : Lab >= 3
            ? ["№", "ID", "Память", Lab >= 4 ? "PC/N" : "PC", "Состояние", "Б/П", "Квант", "Возраст"]
            : ["№", "ID", "Память", "PC", "Состояние"];
        lines.Add(Row(headings, sizes));
        for (int i = 0; i < m.Table.Length; i++)
        {
            var p = m.Table[i];
            string[] cells;
            if (p is null) cells = narrow ? [(i + 1).ToString(), "-", "-", "Свободно", "-"]
                : Lab >= 3 ? [(i + 1).ToString(), "-", "-", "-", "Свободно", "-", "-", "-"]
                : [(i + 1).ToString(), "-", "-", "-", "Свободно"];
            else if (narrow) cells = [(i + 1).ToString(), "P" + p.Id, p.Pc.ToString(), StateName(p.State), Lab >= 3 ? p.Priority.ToString() : "-"];
            else
            {
                var list = new List<string> { (i + 1).ToString(), "P" + p.Id, p.Size.ToString(),
                    Lab >= 4 ? $"{p.Pc}/{p.Program.Length}" : p.Pc.ToString(), StateName(p.State) };
                if (Lab >= 3) list.AddRange([$"{p.BasePriority}/{p.Priority}", p.State == ProcessState.Active ? p.Quantum.ToString() : "-", p.State == ProcessState.Ready ? p.ReadyAge.ToString() : "-"]);
                cells = list.ToArray();
            }
            lines.Add(Row(cells, sizes));
            if (width >= 150 && Lab >= 6 && p is not null)
            {
                lines.Add($"   Такты R/A/IO/ОС/П: {p.ReadyTicks}/{p.ActiveTicks}/{p.IoTicks}/{p.SystemTicks}/{p.SuspendedTicks}"
                    + $" | жизнь {m.Tick - p.Arrival} | T_mono {m.MonoTime(p)} | данные [{string.Join(",", p.Memory)}] | IO-команд {p.IoCount}");
            }
        }
    }
    public static void DrawHistory(SimulationModel m, List<string> lines)
    {
        if (Lab >= 3) lines.Add("История CPU (до 16 назначений): " + Ids(m.Cpu.History));
    }
    public static void DrawMeans(SimulationModel m, List<string> lines)
    {
        if (Lab < 6) return;
        var inputSample = m.Residents.Concat(m.Completed).ToArray();
        lines.Add($"Средние входные: размер {Mean(inputSample, p => p.Size):F1}, команды {Mean(inputSample, p => p.Program.Length):F1}, "
            + $"IO-команд {Mean(inputSample, p => p.IoCount):F1} (резиденты и завершённые).");
        var outputSample = m.Completed.ToArray();
        if (outputSample.Length == 0)
        {
            lines.Add("Средние выходные: пока нет завершённых процессов.");
            return;
        }
        lines.Add($"Средние выходные: T_mono {Mean(outputSample, p => m.MonoTime(p)):F1}, T_multi {Mean(outputSample, p => m.MultiTime(p)):F1}, "
            + $"готовность {Mean(outputSample, p => p.ReadyTicks):F1}, CPU {Mean(outputSample, p => p.ActiveTicks):F1}, "
            + $"IO {Mean(outputSample, p => p.IoTicks):F1}, ОС {Mean(outputSample, p => p.SystemTicks):F1} тактов (завершённые).");
    }
    private static void DrawDetails(SimulationModel m, List<string> lines)
    {
        lines.Add("ПАРАМЕТРЫ СИСТЕМЫ");
        if (Lab >= 3) lines.Add($"Квант: {m.Config.Quantum} полезных такта; повышение приоритета: каждые {m.Config.AgingTicks} тактов ожидания; шаг размера: {m.Config.SizeBand}.");
        if (Lab >= 4)
        {
            lines.Add($"Затраты ОС: загрузка {m.Config.LoadTicks}, переключение {m.Config.DispatchTicks}, запуск IO {m.Config.IoInitTicks}, прерывание IO {m.Config.IoInterruptTicks}, выгрузка {m.Config.FinishTicks}. IO: {m.Config.IoDuration} тактов.");
            lines.Add("Работы ОС: " + string.Join(", ", m.SystemJobs.Select(j => $"{j.Name}, P{j.Owner.Id}, осталось {j.Left}")));
        }
        lines.Add($"CPU: пользовательские такты {m.Cpu.UsefulTicks}, системные {m.Cpu.SystemTicks}, простой {m.Cpu.IdleTicks}.");
        lines.Add($"Производительность: {m.Throughput:F4} заданий/такт; относительная {m.RelativePerformance:F2}%; полезная загрузка CPU {m.UsefulUtilization:F2}%.");
        lines.Add($"Средние завершённых: T_mono {Mean(m.Completed, p => m.MonoTime(p)):F2}, T_multi {Mean(m.Completed, p => m.MultiTime(p)):F2}, ожидание {Mean(m.Completed, p => p.ReadyTicks):F2}.");
        lines.Add("T_mono: время задания в однопрограммной ОС; T_multi: время нахождения в нашей системе.");
        lines.Add("ПОДРОБНОСТИ ПРОЦЕССОВ");
        foreach (var p in m.Residents)
        {
            lines.Add($"P{p.Id}: слот {p.Slot + 1}, состояние {StateName(p.State)} (код {(int)p.State}), память {p.Size}, PC {p.Pc}" + (Lab >= 4 ? $"/{p.Program.Length}" : ""));
            if (Lab >= 3) lines.Add($"  Приоритет базовый/текущий: {p.BasePriority}/{p.Priority}; возраст ожидания {p.ReadyAge}; остаток кванта {p.Quantum}.");
            if (Lab >= 4) lines.Add($"  Операнды: [{string.Join(", ", p.Memory)}]; команд IO {p.IoCount}; загрузка {p.Arrival}, завершение {p.End}.");
            if (Lab >= 6) lines.Add($"  Такты: ожидание {p.ReadyTicks}, CPU {p.ActiveTicks}, IO {p.IoTicks}, ОС {p.SystemTicks}, приостановка {p.SuspendedTicks}; в системе {m.Tick - p.Arrival}.");
            lines.Add($"  T_mono: {m.MonoTime(p)}; T_multi считается после завершения: End - Arrival.");
        }
        lines.Add("ЖУРНАЛ СОБЫТИЙ"); lines.AddRange(m.Events.Select(EventText));
    }
    private static void DrawHelp(List<string> lines)
    {
        lines.Add("F1 — модель; F2 — параметры, статистика и журнал; F3/H — эта справка.");
        lines.Add("PgUp/PgDn — прокрутка текущего экрана. При смене экрана прокрутка сбрасывается.");
        lines.Add("+/- — скорость на 10%; Q — выход; R — сброс. : — ввод команды; Enter — выполнить; Esc — отменить.");
        lines.Add("PC — номер следующей команды; N — число команд, включая Exit. Память — условные единицы.");
        if (Lab >= 3)
        {
            lines.Add("RR: процесс получает квант, затем возвращается в готовность. Квант — полезные такты, не секунды.");
            lines.Add("Q2 — высокий приоритет, Q1 — средний, Q0 — низкий. [-] означает пустую очередь.");
            lines.Add("Высшая непустая очередь — Q2, если в ней есть процессы; иначе Q1; иначе Q0. Выбирается её первый процесс.");
            lines.Add("Б/П — базовый/текущий приоритет; возраст — ожидание в готовности. Большой размер повышает базовый приоритет, ожидание — текущий.");
            lines.Add("Следующий процесс — кандидат при освобождении CPU, а не немедленная замена активного.");
        }
        if (Lab >= 4) lines.Add("Compute — вычисление; Io — ввод/вывод; Exit — завершение. LOAD, DISPATCH, IO_INIT, IO_INTERRUPT, FINISH — работы ОС.");
        if (Lab >= 9)
        {
            lines.Add("КОМАНДЫ ЛР9 (после :)");
            lines.Add("task 128 20 25 — новое задание: размер 128, 20 команд до Exit, вероятность IO 25%.");
            lines.Add("auto off / auto on — запретить / разрешить автозагрузку; резиденты продолжают работать.");
            lines.Add("load — запросить загрузку при освобождении CPU.");
            lines.Add("suspend 3 / resume 3 — приостановить / возобновить P3; kill 3 — удалить его.");
            lines.Add("memory 2048 — размер памяти; нельзя уменьшать ниже занятого объёма.");
            lines.Add("drain — закончить резидентов и выйти; приостановленные сначала возобновить или удалить.");
        }
        lines.Add("Все параметры — на F2. В маленьком окне используйте прокрутку; ~ означает, что число не поместилось в колонку.");
    }
    private static double Mean(IEnumerable<Psw> values, Func<Psw, double> selector)
    {
        var array = values.ToArray(); return array.Length == 0 ? 0 : array.Average(selector);
    }
    private static string EventText(string text) => Regex.Replace(text, @"\b(?:" + string.Join("|", Enum.GetNames<ProcessState>()) + @")\b", match => StateName(Enum.Parse<ProcessState>(match.Value)));
    private static string Ids(IEnumerable<int> ids) { string text = string.Join(",", ids.Select(id => "P" + id)); return text.Length == 0 ? "-" : text; }
    private static string Row(string[] cells, int[] widths) => string.Join(" ", cells.Select((cell, i) => Fit(cell, widths[i]).PadRight(widths[i])));
    private static string Fit(string text, int width) => text.Length <= width ? text : width == 1 ? "~" : text[..(width - 1)] + "~";
    private static IEnumerable<string> Wrap(string text, int width)
    {
        text = text.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');
        while (text.Length > width)
        {
            int cut = text.LastIndexOf(' ', width - 1, width);
            if (cut < width / 2 || cut == 0) cut = width;
            yield return text[..cut]; text = text[cut..].TrimStart();
        }
        yield return text;
    }
}
