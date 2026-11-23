using System.Diagnostics;
using System.Reflection;

namespace MatrixLab5;

internal static class Program
{
    private const long Mod = 1_000_000_007; //чтобы лонг не переполнился при возв. в степень

    private static int Main(string[] args)
    {
        try
        {
            if (args.Length > 0 && args[0] == "--generate") //exe запускается как отдельный процесс-генератор, матрицы в стдаут, основной читает
            {
                GeneratorProcess.Run(int.Parse(args[1]), int.Parse(args[2]), int.Parse(args[3]), int.Parse(args[4]));
                return 0;
            }

            if (args.Length == 0)
            {
                PrintHelp();
                return 0;
            }

            switch (args[0].ToLowerInvariant())
            {
                case "multiply":
                    RunMultiply(args);
                    break;
                case "triangular":
                    RunTriangular(args);
                    break;
                case "power":
                    RunPower(args);
                    break;
                case "benchmark":
                    RunBenchmark(args);
                    break;
                default:
                    PrintHelp();
                    break;
            }
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Ошибка: {ex.Message}");
            return 1;
        } //в стдерр потому что стдаут в режиме generate занят бинарными данными матриц
    }

    private static void PrintHelp()
    {
        Console.WriteLine("""
        MatrixLab5 — лабораторная работа №2

        Команды:
          multiply n m k threads
              Умножение случайных матриц A(n×m) и B(m×k).

          triangular n k threads static|dynamic
              Умножение нижнетреугольной A(n×n) на B(n×k).

          power n p threads
              Возведение квадратной матрицы n×n в степень p.

          benchmark n1 n2 n3 maxThreads
              Серия замеров обычного умножения для трёх размеров.

        Примеры:
          multiply 300 300 300 4
          triangular 500 300 8 dynamic
          power 80 4 4
          benchmark 100 200 300 8
        """);
    }

    private static void RunMultiply(string[] args)
    {
        RequireArgs(args, 5);
        int n = Positive(args[1], "n"), m = Positive(args[2], "m");
        int k = Positive(args[3], "k"), threads = Positive(args[4], "threads");

        Console.WriteLine("Генерация матриц в отдельном процессе...");
        var (a, b) = GeneratorProcess.Generate(n, m, k); //генерируем в отдельном процессе, не в потоке. основной получает через pipe
        Console.WriteLine($"A: {n}×{m}; B: {m}×{k}; потоков: {threads}");

        var sw = Stopwatch.StartNew();
        long[,] c = MatrixOps.Multiply(a, b, threads, showProgress: true);
        sw.Stop(); //замеряется только умножение, монотонный таймер высокого разрешения

        Console.WriteLine($"Время умножения: {sw.Elapsed.TotalSeconds:F4} с");
        Console.WriteLine($"Контрольная сумма: {MatrixOps.Checksum(c)}"); //чтобы компилятор не выбросил вычисления которые не используются. чтобы сверить многопоточный результат с последовательным
        if (n <= 10 && k <= 10) MatrixOps.Print(c);
    }

    private static void RunTriangular(string[] args)
    {
        RequireArgs(args, 5);
        int n = Positive(args[1], "n"), k = Positive(args[2], "k");
        int threads = Positive(args[3], "threads");
        string mode = args[4].ToLowerInvariant();
        if (mode is not ("static" or "dynamic"))
            throw new ArgumentException("Режим должен быть static или dynamic.");

        long[][] a = MatrixOps.CreateLowerTriangular(n); //long[][] тк строки разной длины, нули выше диагонали не хранятся
        long[,] b = MatrixOps.RandomMatrix(n, k);
        var sw = Stopwatch.StartNew();
        long[,] c = TriangularOps.Multiply(a, b, threads, mode == "dynamic", true); // выбирается параметром динамик, который приходит из аргумента команд. строки
        sw.Stop();

        Console.WriteLine($"Режим: {mode}; время: {sw.Elapsed.TotalSeconds:F4} с");
        Console.WriteLine($"Контрольная сумма: {MatrixOps.Checksum(c)}");
        if (n <= 10 && k <= 10) MatrixOps.Print(c);
    }

    private static void RunPower(string[] args)
    {
        RequireArgs(args, 4);
        int n = Positive(args[1], "n"), p = Positive(args[2], "p");
        int threads = Positive(args[3], "threads");

        // Работа по модулю ограничивает рост чисел и предотвращает переполнение long.
        long[,] a = MatrixOps.RandomMatrix(n, n);
        var sw = Stopwatch.StartNew();
        long[,] result = PowerOps.Raise(a, p, threads, Mod, true); // умножения R1 = A, дальше R(s+1) = Rs*A
        sw.Stop();

        Console.WriteLine($"A^{p}; размер: {n}×{n}; потоков: {threads}");
        Console.WriteLine($"Время возведения в степень: {sw.Elapsed.TotalSeconds:F4} с");
        Console.WriteLine($"Контрольная сумма по модулю {Mod}: {MatrixOps.ChecksumMod(result, Mod)}"); //checksumMod тк элементы уже по модулю, без модуля сумма переполнится. сравнивать удобнее в этом же поле
        if (n <= 10) MatrixOps.Print(result);
    }

    private static void RunBenchmark(string[] args)
    {
        RequireArgs(args, 5);
        int[] sizes = { Positive(args[1], "n1"), Positive(args[2], "n2"), Positive(args[3], "n3") };
        int maxThreads = Positive(args[4], "maxThreads");
        Console.WriteLine("Размер;Потоки;Время_сек;Контрольная_сумма");

        foreach (int n in sizes)
        {
            long[,] a = MatrixOps.RandomMatrix(n, n);
            long[,] b = MatrixOps.RandomMatrix(n, n);
            // Прогрев JIT не включается в измерения.
            _ = MatrixOps.Multiply(a, b, 1, false);

            for (int t = 1; t <= maxThreads; t++)
            {
                var samples = new List<double>();
                long checksum = 0;
                for (int repetition = 0; repetition < 5; repetition++)
                {
                    var sw = Stopwatch.StartNew();
                    long[,] c = MatrixOps.Multiply(a, b, t, false); //прогрев JIT компилирует код при первом вызове, первый замер был бы завышен. этот вызов не учитывается
                    sw.Stop();
                    samples.Add(sw.Elapsed.TotalSeconds);
                    checksum = MatrixOps.Checksum(c);
                }

                samples.Sort();
                double average = samples.Skip(1).Take(3).Average();
                Console.WriteLine($"{n};{t};{average:F6};{checksum}");
                //замеры обрабатываются по 5 штук, отбрасывамем min max, усредняем 3
            }
        }
    }

    private static int Positive(string value, string name)
    {
        if (!int.TryParse(value, out int result) || result < 1)
            throw new ArgumentException($"{name} должно быть целым числом не меньше 1.");
        return result;
    }

    private static void RequireArgs(string[] args, int count)
    {
        if (args.Length != count)
            throw new ArgumentException("Неверное число аргументов. Запустите программу без аргументов для справки.");
    }
}

internal static class MatrixOps
{
    private static readonly Random SharedRandom = new();
    private static readonly object RandomLock = new();

    public static long[,] RandomMatrix(int rows, int cols)
    {
        var matrix = new long[rows, cols];
        lock (RandomLock) //рандом не потокообразен. лок как подстраховка(генерация в главном потоке)
        {
            for (int i = 0; i < rows; i++)
                for (int j = 0; j < cols; j++)
                    matrix[i, j] = SharedRandom.Next(0, 10); //рекомендация п. 1.4, маленький диапазон, чтобы не переполнилось при умножении
        }
        return matrix;
    }

    public static long[][] CreateLowerTriangular(int n)
    {
        var a = new long[n][];
        lock (RandomLock)
        {
            for (int i = 0; i < n; i++)
            {
                a[i] = new long[i + 1];
                for (int j = 0; j <= i; j++)
                    a[i][j] = SharedRandom.Next(0, 10);
            }
        }
        return a;
    }

    public static long[,] Multiply(long[,] a, long[,] b, int threadCount, bool showProgress)
    {
        int n = a.GetLength(0), m = a.GetLength(1), k = b.GetLength(1);
        if (m != b.GetLength(0))
            throw new ArgumentException("Число столбцов A должно совпадать с числом строк B.");

        var result = new long[n, k];
        int workers = Math.Min(threadCount, n); //если потоков больше чем строк, лишние не создаются
        int completedRows = 0, lastPercent = -1;
        object progressLock = new();

        void Worker(int workerId)
        {
            int start = workerId * n / workers;
            int end = (workerId + 1) * n / workers; //работа делится статически, непрерывными блоками. автоматически распределяется остаток(если н не делится на т, остаток тоже распределить)

            for (int i = start; i < end; i++)
            {
                // Порядок i -> t -> j: чтение B и запись C идут по строкам.
                //какой порядок циклов и почему? → i → t → j. Внутренний цикл идёт подряд по строке b[t, j] и по строке result[i, j] — они попадают в кэш-линию (64 байта). 
                //Наивный i → j → t читал бы b[t, j] по столбцу, каждый элемент — в разных кэш-линиях, промах кэша на каждом шаге, в разы медленнее (п. 2.4). value = a[i, t] вынесен — одно чтение на t.
                for (int t = 0; t < m; t++)
                {
                    long value = a[i, t];
                    for (int j = 0; j < k; j++)
                        result[i, j] += value * b[t, j]; //result += тк строки делятся между потоками, каждый поток пишет только свои строки, поэтому гонки нет и не надо обнулять
                }

                int done = Interlocked.Increment(ref completedRows); //Interlocked тк счетчик общий, к нему обращаются все потоки.
                if (showProgress)
                {
                    int percent = done * 100 / n;
                    lock (progressLock)
                    {
                        if (percent > lastPercent)
                        {
                            lastPercent = percent; //под локом - иначе гонка на нём
                            Console.Write($"\rПрогресс: {percent}%   ");
                            if (percent == 100) Console.WriteLine();
                        } //не печатаем каждый раз тк вывод в консоль медленный, съест время умножения. Достаточно при смене целого процента. 
                    }
                }
            }
        }

        RunThreads(workers, Worker); //новые потоки создаются new Thread, вручную, Start + join. 
        return result;
    }

    public static long Checksum(long[,] matrix)
    {
        long sum = 0;
        foreach (long value in matrix) sum = unchecked(sum + value);
        return sum;
    }

    public static long ChecksumMod(long[,] matrix, long modulus)
    {
        long sum = 0;
        foreach (long value in matrix) sum = (sum + value) % modulus;
        return sum;
    }

    public static void Print(long[,] matrix)
    {
        for (int i = 0; i < matrix.GetLength(0); i++)
        {
            for (int j = 0; j < matrix.GetLength(1); j++)
                Console.Write($"{matrix[i, j],8}");
            Console.WriteLine();
        }
    }

    public static void RunThreads(int count, Action<int> action)
    {
        var threads = new Thread[count];
        for (int i = 0; i < count; i++)
        {
            int captured = i;
            threads[i] = new Thread(() => action(captured));
            threads[i].Start();
        }
        foreach (Thread thread in threads) thread.Join();
    }
}

internal static class TriangularOps
{
    public static long[,] Multiply(long[][] a, long[,] b, int threadCount, bool dynamic, bool showProgress)
    {
        int n = a.Length, k = b.GetLength(1);
        if (b.GetLength(0) != n) throw new ArgumentException("Размеры матриц несовместимы.");

        var result = new long[n, k];
        int workers = Math.Min(threadCount, n);
        int nextRow = -1, completed = 0, lastPercent = -1;
        object rowLock = new(), progressLock = new();

        void CalculateRow(int i)
        {
            for (int t = 0; t <= i; t++) // до i, тк строка треуг. матрицы содержит i+1 элемент(1 строка - умножение, последняя n)
            {
                long value = a[i][t];
                for (int j = 0; j < k; j++)
                    result[i, j] += value * b[t, j];
            }

            int done = Interlocked.Increment(ref completed);
            if (showProgress)
            {
                int percent = done * 100 / n;
                lock (progressLock)
                {
                    if (percent > lastPercent)
                    {
                        lastPercent = percent;
                        Console.Write($"\rПрогресс: {percent}%   ");
                        if (percent == 100) Console.WriteLine();
                    }
                }
            }
        }

        void Worker(int id)
        {
            if (!dynamic)
            {
                int start = id * n / workers; //проблема статического на треугольной - нагрузка неравномерна. нижние строки в n раз тяжелее верхних. поток с верхними строками закончит раньше и будет простаивать
                int end = (id + 1) * n / workers;
                for (int i = start; i < end; i++) CalculateRow(i);
            }
            else
            {
                while (true)
                {
                    int row;
                    lock (rowLock) row = ++nextRow; //лок иначе два потока возьмут одну строку(неверный результат)
                    if (row >= n) break;
                    CalculateRow(row);
                } //общий счетчик nextRow, поток под локом берет след. строку, считает, бурет след. Тяжелые нижние строки достаются тому, кто освободился - балансировка нагрузки. динамик быстрее статик на треугольной
            }
        }

        MatrixOps.RunThreads(workers, Worker);
        return result;
    }
}

internal static class PowerOps
{
    public static long[,] Raise(long[,] a, int exponent, int threadCount, long modulus, bool showProgress)
    {
        if (a.GetLength(0) != a.GetLength(1))
            throw new ArgumentException("Для возведения в степень нужна квадратная матрица.");
        if (exponent < 1) throw new ArgumentException("Степень должна быть не меньше 1.");

        int n = a.GetLength(0);
        long[,] current = (long[,])a.Clone();
        if (exponent == 1) return current;

        long[,] next = new long[n, n];
        int workers = Math.Max(1, threadCount);
        int completedRows = 0, lastPercent = -1;
        object progressLock = new();

        using var barrier = new Barrier(workers, _ =>
        {
            // Callback выполняется ровно один раз после завершения всеми потоками шага.
            (current, next) = (next, current); //барьер тк шаг s+1 читает результат шага s целиком, поэтому начинать его можно только тогда, когда все потоки закончили шаг s. Barrier ждет всех, потом вызывает callback 
        });

        MatrixOps.RunThreads(workers, workerId =>
        {
            int start = workerId * n / workers;
            int end = (workerId + 1) * n / workers;

            for (int step = 0; step < exponent - 1; step++)
            {
                for (int i = start; i < end; i++)
                {
                    // После предыдущей смены буферов next содержит старые данные.
                    // Обнуляем свою строку перед записью нового произведения.
                    for (int j = 0; j < n; j++) next[i, j] = 0;

                    for (int t = 0; t < n; t++)
                    {
                        long value = current[i, t];
                        for (int j = 0; j < n; j++)
                            next[i, j] = (next[i, j] + value * current[t, j]) % modulus;
                    }

                    int done = Interlocked.Increment(ref completedRows);
                    if (showProgress)
                    {
                        int total = (exponent - 1) * n;
                        int percent = done * 100 / total;
                        lock (progressLock)
                        {
                            if (percent > lastPercent)
                            {
                                lastPercent = percent;
                                Console.Write($"\rПрогресс: {percent}%   ");
                                if (percent == 100) Console.WriteLine();
                            }
                        }
                    }
                }

                barrier.SignalAndWait();
            }
        });

        return current;
    }
}

internal static class GeneratorProcess
{
    public static (long[,] A, long[,] B) Generate(int n, int m, int k)
    {
        string executable = Environment.ProcessPath
            ?? throw new InvalidOperationException("Не удалось определить путь к исполняемому файлу.");
        bool isDotnetHost = Path.GetFileNameWithoutExtension(executable)
            .Equals("dotnet", StringComparison.OrdinalIgnoreCase);

        var start = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        if (isDotnetHost)
            start.ArgumentList.Add(Assembly.GetEntryAssembly()!.Location);

        start.ArgumentList.Add("--generate");
        start.ArgumentList.Add(n.ToString());
        start.ArgumentList.Add(m.ToString());
        start.ArgumentList.Add(k.ToString());
        start.ArgumentList.Add(Environment.TickCount.ToString());

        using Process process = Process.Start(start)
            ?? throw new InvalidOperationException("Не удалось запустить процесс-генератор.");

        // Двоичные данные читаются из stdout; текстовые сообщения дочерний процесс не печатает.
        using var reader = new BinaryReader(process.StandardOutput.BaseStream);
        int rowsA = reader.ReadInt32(), colsA = reader.ReadInt32();
        var a = new long[rowsA, colsA];
        for (int i = 0; i < rowsA; i++)
            for (int j = 0; j < colsA; j++)
                a[i, j] = reader.ReadInt64();

        int rowsB = reader.ReadInt32(), colsB = reader.ReadInt32();
        var b = new long[rowsB, colsB];
        for (int i = 0; i < rowsB; i++)
            for (int j = 0; j < colsB; j++)
                b[i, j] = reader.ReadInt64();

        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException("Процесс-генератор завершился с ошибкой: " + process.StandardError.ReadToEnd());

        return (a, b);
    }

    public static void Run(int n, int m, int k, int seed)
    {
        var random = new Random(seed);
        using var writer = new BinaryWriter(Console.OpenStandardOutput());
        writer.Write(n);
        writer.Write(m);
        for (int i = 0; i < n; i++)
            for (int j = 0; j < m; j++)
                writer.Write((long)random.Next(0, 10));

        writer.Write(m);
        writer.Write(k);
        for (int i = 0; i < m; i++)
            for (int j = 0; j < k; j++)
                writer.Write((long)random.Next(0, 10));
        writer.Flush();
    }
}