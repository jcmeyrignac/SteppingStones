using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Security.Policy;
using System.Text;
using System.Threading;

namespace ReduceBS
{
    /*
     * algorithm:
     * 
     * - we load a solutions file
     * - for each solution, we reduce to M stones
     * - we check that the hash of this position has not been computed
     * - then we extend to N stones
     * 

        Add Reduction algorithm (remove one cell)

     */
    public enum Optimizer
    {
        BeamSearch,
        ExhaustiveSearch,
        RefillBeamSearch,
        RefillExhaustiveSearch,
        ReduceBeamSearch,
        ReduceExhaustiveSearch,
        PartialBeamSearch,
        PartialExhaustiveSearch
    }

    public class ReduceBS
    {
        private static bool useBeamCompressed;               // true=use BeamCompressed.c, false=use Beam.c
        private static bool usePattern;
        private static readonly bool useCacheAroundOnes = true;                   // we only compute the grids with the minimal AroundOnes
        public static readonly bool useMinimalAroundOnes = true;
        public static readonly int maxGridsInMemory = 12;
        public static int MinimumStones;
        public static int order;
        public static int minimumAroundOnes;
        public const int MaximumStones = 0;
        private static int beamSize;
        private static States saveStates;
        private static Optimizer method;
        private static string prefix;
        private const int maxThreads = 2;                  // maximum number of threads
        private const int maxThreadsExhaustive = 12;                  // maximum number of threads
        private static ConcurrentQueue<KeyValuePair<string, string>> _queue;
        private static readonly object saveLock = new object();
        private static int seconds;
        private static int _totalPositions;
        private static volatile int currentPosition;
        private const string compilerPath = @"C:\Program Files\LLVM\bin\clang-cl.exe";
        private const string arguments = "/arch:AVX2 /Fe{1} /fp:fast /Gr /GS- /MT /O2 /Oi /Ot /W4 /Fa{3} {0} {2}";
        private static volatile int _record;
        private static Dictionary<string, int> cacheAroundOnes;
#if RANDOMIZE_HASH
        private static readonly Random getrandom = new Random();
#endif
        static void Main(string[] args)
        {
            int step = 1;
            int countArguments = 0;
            bool refill = false;
            bool reduce = false;
            bool partial = false;
            beamSize = 0;
            cacheAroundOnes = new Dictionary<string, int>();
            foreach (var a in args)
            {
                if (a == "-f")
                {
                    refill = true;
                    continue;
                }
                if (a == "-r")
                {
                    reduce = true;
                    continue;
                }
                if (a == "-p")
                {
                    partial = true;
                    continue;
                }
                switch (countArguments)
                {
                    case 0:
                        order = Convert.ToInt32(a);
                        ++countArguments;
                        break;
                    case 1:
                        MinimumStones = Convert.ToInt32(a);
                        ++countArguments;
                        break;
                    case 2:
                        beamSize = Convert.ToInt32(a);
                        ++countArguments;
                        break;
                    case 3:
                        step = Convert.ToInt32(a);
                        ++countArguments;
                        break;
                    default:
                        throw new Exception("Incorrect argument");
                }
            }

            useBeamCompressed = File.Exists("BeamCompressed.c");
            usePattern = File.Exists("pattern.h");

            if (countArguments >= 3 && !refill && !reduce && !partial)
            {
                method = Optimizer.BeamSearch;
                prefix = "ExtendBS";
                BuildExecutable(order, useBeamCompressed ? "BeamCompressed.c" : "Beam.c");
            }
            else if (countArguments >= 3 && refill)
            {
                method = Optimizer.RefillBeamSearch;
                prefix = "RefillBS";
                BuildExecutable(order, useBeamCompressed ? "BeamCompressed.c" : "Beam.c");
            }
            else if (countArguments >= 3 && partial)
            {
                method = Optimizer.PartialBeamSearch;
                prefix = "PartialBS";
                BuildExecutable(order, useBeamCompressed ? "BeamCompressed.c" : "Beam.c");
            }
            else if (countArguments >= 3 && reduce)
            {
                method = Optimizer.ReduceBeamSearch;
                prefix = "ReduceBS";
                BuildExecutable(order, useBeamCompressed ? "BeamCompressed.c" : "Beam.c");
            }
            else if (countArguments == 2 && !refill && !reduce && !partial)
            {
                method = Optimizer.ExhaustiveSearch;
                prefix = "Extend";
                BuildExecutable(order, "Extend.c");
            }
            else if (countArguments == 2 && partial)
            {
                method = Optimizer.PartialExhaustiveSearch;
                prefix = "Partial";
                BuildExecutable(order, "Extend.c");
            }
            else if (countArguments == 2 && refill)
            {
                method = Optimizer.RefillExhaustiveSearch;
                prefix = "Refill";
                BuildExecutable(order, "Extend.c");
            }
            else if (countArguments == 2 && reduce)
            {
                method = Optimizer.ReduceExhaustiveSearch;
                prefix = "Reduce";
                BuildExecutable(order, "Extend.c");
            }
            else
            {
                throw new Exception("Usage");
            }

            Console.WriteLine(prefix);
            Grid.BuildOffsets();

            while (MinimumStones > 0)
            {
                Console.WriteLine("MinimumStones={0}", MinimumStones);
                saveStates = new States(prefix, order, beamSize, MinimumStones, MaximumStones);
                _record = saveStates.Load();

                _totalPositions = 0;
                _queue = new ConcurrentQueue<KeyValuePair<string, string>>();

                minimumAroundOnes = 1 << 30;
                foreach (var f in Directory.GetFiles(Directory.GetCurrentDirectory(), "soutput*.txt"))
                {
                    LoadSolutions(f);
                }
                _totalPositions = _queue.Count;
                currentPosition = 0;

                int threads = maxThreads;
                if (IsExhaustiveSearch() || method == Optimizer.PartialBeamSearch)
                    threads = maxThreadsExhaustive;

                var tasks = new Thread[threads];
                for (int i = 0; i < threads; ++i)
                {
                    tasks[i] = new Thread(ThreadWithState);
                    tasks[i].Start(i);
                }
                for (int i = 0; i < threads; ++i)
                {
                    tasks[i].Join();
                    tasks[i] = null;
                }

                if (method == Optimizer.PartialExhaustiveSearch)
                {
                    Console.WriteLine("{0} {1}", MinimumStones, _totalPositions);
                    --MinimumStones;
                    continue;
                }

                if (IsExhaustiveSearch())
                    break;
                MinimumStones -= step;
            }
        }

        private static bool IsExhaustiveSearch()
        {
            return method == Optimizer.ExhaustiveSearch || method == Optimizer.RefillExhaustiveSearch || method == Optimizer.ReduceExhaustiveSearch || method == Optimizer.PartialExhaustiveSearch;
        }

        private static void BuildExecutable(int maxOrder, string inputFile)
        {
            bool refill = method == Optimizer.RefillExhaustiveSearch || method == Optimizer.RefillBeamSearch || method == Optimizer.ReduceExhaustiveSearch || method == Optimizer.ReduceBeamSearch;
            bool partial = method == Optimizer.PartialExhaustiveSearch || method == Optimizer.PartialBeamSearch;
            string prefix = GetExecutablePrefix();

            string output = string.Format("{0}{1}.exe", prefix, maxOrder);
            if (File.Exists(output))
                return;
            string outputAsm = string.Format("{0}{1}.asm", prefix, maxOrder);

            var sb = new StringBuilder();
            sb.Append(" -D NN=").Append(maxOrder);
            sb.Append(" -D USE_RANDOMIZED_SCORING=").Append(0);
            sb.Append(" -D USE_PATTERN=").Append(usePattern ? 1 : 0);
            sb.Append(" -D REFILL=").Append(refill ? 1 : 0);
            sb.Append(" -D PARTIAL=").Append(partial ? 1 : 0);
            string args = string.Format(arguments, sb.ToString(), output, inputFile, outputAsm);
            Console.WriteLine(compilerPath + " " + args);

            var process = new ProcessStartInfo(compilerPath)
            {
                Arguments = args,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            Process.Start(process).WaitForExit();
            if (!File.Exists(output))
                throw new Exception("Missing exe !");
        }


        static int CountAroundOnes(List<string> grid)
        {
            int SIZE = 128;
            var stones = new int[SIZE * SIZE];
            var positionOnes = new List<int>();

            for (int y = 0; y < grid.Count; y++)
            {
                string line = grid[y];
                var parts = line.Split(' ');
                for (int x = 0; x < parts.Length; ++x)
                {
                    int c = Convert.ToInt32(parts[x]);
                    if (c != 0)
                    {
                        int offset = y * SIZE + x;
                        stones[offset] = c;
                        if (c == 1)
                            positionOnes.Add(offset);
                    }
                }
            }
            int countCellsAroundOnes = 0;
            for (int st = 0; st < positionOnes.Count; ++st)
            {
                int p = positionOnes[st];
                for (int dy = -1; dy <= 1; ++dy)
                {
                    for (int dx = -1; dx <= 1; ++dx)
                    {
                        if (dx == 0 && dy == 0) continue;
                        int newP = p + dx + dy * SIZE;

                        if (newP >= 0 && newP < SIZE * SIZE && stones[newP] > 1)
                            ++countCellsAroundOnes;
                    }
                }
            }
            return countCellsAroundOnes;
        }

        static void LoadSolutions(string filename)
        {
            var grid = new List<string>();
            if (useCacheAroundOnes)
            {
                if (!cacheAroundOnes.ContainsKey(filename))
                {
                    int countAroundOnes = 1 << 30;
                    //int countAroundOnes = 0;
                    using (var sr = new StreamReader(filename, false))
                    {
                        while (!sr.EndOfStream)
                        {
                            string line = sr.ReadLine().Trim();
                            if (line.StartsWith("Solution") || line.Contains(","))
                            {
                                if (grid.Count != 0)
                                {
                                    int n = CountAroundOnes(grid);
                                    countAroundOnes = Math.Min(countAroundOnes, n);
                                    //countAroundOnes = Math.Max(countAroundOnes, n);
                                    grid.Clear();
                                }
                            }
                            else
                            {
                                grid.Add(line);
                            }
                        }
                    }
                    if (grid.Count != 0)
                    {
                        int n = CountAroundOnes(grid);
                        countAroundOnes = Math.Min(countAroundOnes, n);
                        //countAroundOnes = Math.Max(countAroundOnes, n);
                        grid.Clear();
                    }
                    cacheAroundOnes.Add(filename, countAroundOnes);
                    Console.WriteLine("AroundOnes={1} in {0}", filename, countAroundOnes);
                }
            }

            using (var sr = new StreamReader(filename, false))
            {
                while (!sr.EndOfStream)
                {
                    string line = sr.ReadLine().Trim();
                    if (line.StartsWith("Solution") || line.Contains(","))
                    {
                        if (grid.Count != 0)
                        {
                            if (!useCacheAroundOnes || CountAroundOnes(grid) <= cacheAroundOnes[filename])
                            {
                                var g = new Grid();
                                g.Parse(grid, method, saveStates, _queue);
                            }
                            grid.Clear();
                        }
                    }
                    else
                    {
                        grid.Add(line);
                    }
                }
            }
            if (grid.Count != 0)
            {
                if (!useCacheAroundOnes || CountAroundOnes(grid) <= cacheAroundOnes[filename])
                {
                    var g = new Grid();
                    g.Parse(grid, method, saveStates, _queue);
                }
            }
        }
        private static string ContextFolder(int thread)
        {
            return string.Format("Run{0:D5}", thread);
        }

        private static void ThreadWithState(object o)
        {
            int slot = (int)o;
            KeyValuePair<string, string> ent;
            while (_queue.TryDequeue(out ent))
            {
                string hash = ent.Key;
                string grille = ent.Value;
                string folder = ContextFolder(slot);
                var d = DateTime.Now.Second;
                ++currentPosition;
                if (d != seconds)
                {
                    seconds = d;
                    Console.Write("{0}/{1}/{2}/{3} {4}\r", currentPosition, _totalPositions, slot, MinimumStones, _record);
                }
                Directory.CreateDirectory(folder);

                string workFile = "input.txt";
                using (var sw = new StreamWriter(Path.Combine(folder, workFile)))
                {
                    sw.Write(grille);
                }

                var sb = new StringBuilder();
                string executable;

                if (method == Optimizer.BeamSearch || method == Optimizer.RefillBeamSearch || method == Optimizer.ReduceBeamSearch || method == Optimizer.PartialBeamSearch)
                {
                    sb.Append(RandomizeBeamSize());
                    sb.Append(' ').Append(workFile);
                    sb.Append(' ').Append(MinimumStones);
                    sb.Append(' ').Append(MaximumStones);
                    executable = Path.Combine(Directory.GetCurrentDirectory(), GetExecutablePrefix() + order + ".exe");
                }
                else if (IsExhaustiveSearch())
                {
                    sb.Append(workFile);
                    sb.Append(' ').Append(order);
                    executable = Path.Combine(Directory.GetCurrentDirectory(), GetExecutablePrefix() + order + ".exe");
                }
                else
                {
                    return;
                }
                var process = new ProcessStartInfo(executable)
                {
                    Arguments = sb.ToString(),
                    WindowStyle = ProcessWindowStyle.Hidden,
                    WorkingDirectory = folder
                };
                var proc = Process.Start(process);
                proc.WaitForExit();
                int result = proc.ExitCode;
                _record = Math.Max(_record, result);
                lock (saveLock)
                {
                    saveStates.SaveHash(hash, result);
                }
            }
        }

        static string GetExecutablePrefix()
        {
            switch (method)
            {
                case Optimizer.ExhaustiveSearch:
                    return "Exhaustive";
                case Optimizer.RefillExhaustiveSearch:
                case Optimizer.ReduceExhaustiveSearch:
                    // same program
                    return "ExhaustiveRefill";
                case Optimizer.BeamSearch:
                    return "Beam";
                case Optimizer.RefillBeamSearch:
                case Optimizer.ReduceBeamSearch:
                    // same program
                    return "Refill";
                case Optimizer.PartialBeamSearch:
                    return "Partial";
                case Optimizer.PartialExhaustiveSearch:
                    return "ExhaustivePartial";
                default:
                    throw new Exception("Unknown method");
            }
        }

        static int RandomizeBeamSize()
        {
#if !RANDOMIZE_HASH
            return beamSize;
#else
            return getrandom.Next((int)(beamSize * 0.99), beamSize);
#endif
        }
    }
    public class Grid
    {
        const int SIZE = 128;
        public struct Coordinate
        {
            public int x, y;
        };

        private int[] sums;
        public int[] rebuiltStones;
        private int[] stones;
        private static readonly Coordinate[] offsets = new Coordinate[SIZE * SIZE];
        int xmin, xmax, ymin, ymax;

        public static void BuildOffsets()
        {
            for (int y = 0; y < SIZE; ++y)
            {
                for (int x = 0; x < SIZE; ++x)
                {
                    int o = y * SIZE + x;
                    offsets[o].x = x;
                    offsets[o].y = y;
                }
            }
        }

        public void Parse(List<string> grid, Optimizer method, States saveStates, ConcurrentQueue<KeyValuePair<string, string>> _queue)
        {
            stones = new int[SIZE * SIZE];
            var stonesPositions = new Dictionary<int, int>();
            var onePositions = new List<int>();
            for (int y = 0; y < grid.Count; y++)
            {
                string line = grid[y];
                var parts = line.Split(' ');
                for (int x = 0; x < parts.Length; ++x)
                {
                    int c = Convert.ToInt32(parts[x]);
                    if (c != 0)
                    {
                        int offset = y * SIZE + x;
                        stones[offset] = c;
                        if (c == 1)
                        {
                            onePositions.Add(offset);
                        }
                        else
                        {
                            stonesPositions.Add(c, offset);
                        }
                    }
                }
            }

            // is the grid too small?
            if (!stonesPositions.ContainsKey(ReduceBS.MinimumStones))
                return;
            // now, we rebuild the grid, by placing stones starting with 1
            rebuiltStones = new int[SIZE * SIZE];
            sums = new int[SIZE * SIZE];

            switch (method)
            {
                case Optimizer.BeamSearch:
                case Optimizer.ExhaustiveSearch:
                    RebuildNormalGrid(stonesPositions);
                    QueueGrid(saveStates, _queue);
                    break;
                case Optimizer.RefillBeamSearch:
                case Optimizer.RefillExhaustiveSearch:
                    RebuildRefillGrid(onePositions, stonesPositions);
                    QueueGrid(saveStates, _queue);
                    break;
                case Optimizer.ReduceExhaustiveSearch:
                case Optimizer.ReduceBeamSearch:
                    RebuildRefillGrid(onePositions, stonesPositions);
                    for (int cell = 0; cell < rebuiltStones.Length; ++cell)
                    {
                        if (rebuiltStones[cell] == 1)
                        {
                            if (IsRemovable(cell))
                            {
                                rebuiltStones[cell] = 0;
                                QueueGrid(saveStates, _queue);
                                rebuiltStones[cell] = 1;
                            }
                        }
                    }
                    break;
                case Optimizer.PartialExhaustiveSearch:
                case Optimizer.PartialBeamSearch:
                    RebuildPartialGrid(onePositions, stonesPositions, saveStates, _queue);
                    break;
                default:
                    throw new Exception("Unknown reduction");
            }
        }

        private void QueueGrid(States saveStates, ConcurrentQueue<KeyValuePair<string, string>> _queue)
        {
            if (ReduceBS.useMinimalAroundOnes)
            {
                int countCellsAroundOnes = 0;
                for (int i = 0; i < SIZE * SIZE; ++i)
                {
                    if (rebuiltStones[i] == 1)
                    {
                        for (int dy = -1; dy <= 1; ++dy)
                        {
                            for (int dx = -1; dx <= 1; ++dx)
                            {
                                if (dx == 0 && dy == 0) continue;
                                int newP = i + dx + dy * SIZE;
                                if (newP >= 0 && newP < SIZE * SIZE && rebuiltStones[newP] > 1)
                                    ++countCellsAroundOnes;
                            }
                        }
                    }
                }
                if (countCellsAroundOnes > ReduceBS.minimumAroundOnes)
                {
                    return;
                }
                if (countCellsAroundOnes < ReduceBS.minimumAroundOnes)
                {
                    ReduceBS.minimumAroundOnes = countCellsAroundOnes;
                    _queue = new ConcurrentQueue<KeyValuePair<string, string>>();
                    Console.WriteLine("Minimum={0}", countCellsAroundOnes);
                }
            }

            if (ComputeBounds() > ReduceBS.order)
                return;

            if (ReduceBS.maxGridsInMemory != 0 && _queue.Count >= ReduceBS.maxGridsInMemory)
                return;

            string hash = GetHash();
            if (!saveStates.hashes.Contains(hash))
            {
                saveStates.hashes.Add(hash);
                _queue.Enqueue(new KeyValuePair<string, string>(hash, GetContent()));
            }
}


private void RebuildNormalGrid(Dictionary<int, int> stonesPositions)
{
    for (int stone = 2; stone <= ReduceBS.MinimumStones; ++stone)
    {
        int pos = stonesPositions[stone];
        int x = offsets[pos].x;
        int y = offsets[pos].y;
        if (sums[pos] != stone)
        {
            for (int dy = -1; dy <= 1; ++dy)
            {
                for (int dx = -1; dx <= 1; ++dx)
                {
                    if (dx == 0 && dy == 0)
                        continue;
                    // we search all the ones around this cell, and copy them
                    int o = pos + dy * SIZE + dx;
                    if (x + dx >= 0 && y + dy >= 0 && x + dx < SIZE && y + dy < SIZE && rebuiltStones[o] == 0 && stones[o] == 1)
                    {
                        PropagateSum(o, 1);
                    }
                }
            }
        }
        // propagate the sum
        Debug.Assert(sums[pos] == stone);
        PropagateSum(pos, stone);
    }
}

private void RebuildRefillGrid(List<int> onePositions, Dictionary<int, int> stonesPositions)
{
    foreach (var pos in onePositions)
    {
        PropagateSum(pos, 1);
    }

    for (int stone = 2; stone <= ReduceBS.MinimumStones; ++stone)
    {
        int pos = stonesPositions[stone];
        //int x = offsets[pos].x;
        //int y = offsets[pos].y;
        Debug.Assert(sums[pos] == stone);
        PropagateSum(pos, stone);
    }
}

private void RebuildPartialGrid(List<int> onePositions, Dictionary<int, int> stonesPositions, States saveStates, ConcurrentQueue<KeyValuePair<string, string>> _queue)
{
    var tempStones = new int[SIZE * SIZE];
    // we rebuild the grid
    foreach (var pos in onePositions)
    {
        tempStones[pos] = 1;
    }
    for (int stone = 2; stone < stonesPositions.Count + 2; ++stone)
    {
        int pos = stonesPositions[stone];
        tempStones[pos] = stone;
    }

    RecursiveRemovePartial(tempStones, onePositions, stonesPositions[2], 0, saveStates, _queue);
}

private void RecursiveRemovePartial(int[] inputStones, List<int> onePositions, int positionOf2, int start, States saveStates, ConcurrentQueue<KeyValuePair<string, string>> _queue)
{
    // we propagate the inputStones, starting with stone 2
    rebuiltStones = new int[SIZE * SIZE];
    RecursivelyCopy(inputStones, rebuiltStones, positionOf2);

    int count = 0;
    for (int i = 0; i < SIZE * SIZE; ++i)
        if (rebuiltStones[i] > 1)
            ++count;

    // if the count is equal to MinimumStones, then we store the position
    if (count == ReduceBS.MinimumStones)
    {
        //int ones = ComputeBounds();
        //for (int y = ymin; y <= ymax; ++y)
        //{
        //    for (int x = xmin; x <= xmax; ++x)
        //    {
        //        Console.Write(rebuiltStones[y * SIZE + x].ToString("D3"));
        //        Console.Write(" ");
        //    }
        //    Console.WriteLine();
        //}
        //Console.WriteLine("{0} {1}", ones, count);
        QueueGrid(saveStates, _queue);
    }
    // if it's below, we don't search further
    if (count <= ReduceBS.MinimumStones)
        return;

    for (int rem = start; rem < onePositions.Count; ++rem)
    {
        var tempStones = (int[])inputStones.Clone();
        int pos = onePositions[rem];
        Debug.Assert(tempStones[pos] == 1);
        // we remove the 1
        tempStones[pos] = 0;
        // and then recursively remove all adjacent ones if they are greater than the previous value
        RemoveRecursively(tempStones, pos, 1);
        RecursiveRemovePartial(tempStones, onePositions, positionOf2, rem + 1, saveStates, _queue);
    }
}
private void RemoveRecursively(int[] grid, int pos, int stone)
{
    for (int dy = -1; dy <= 1; ++dy)
    {
        for (int dx = -1; dx <= 1; ++dx)
        {
            if (dx == 0 && dy == 0) continue;

            int newX = offsets[pos].x + dx;
            int newY = offsets[pos].y + dy;
            if (newX >= 0 && newX < SIZE && newY >= 0 && newY < SIZE)
            {
                int newOffset = newY * SIZE + newX;
                int c = grid[newOffset];
                if (c > stone)
                {
                    grid[newOffset] = 0;
                    RemoveRecursively(grid, newOffset, c);
                }
            }
        }
    }
}

private void RecursivelyCopy(int[] sourceGrid, int[] destGrid, int pos)
{
    if (sourceGrid[pos] == 0 || destGrid[pos] != 0)
        return;
    destGrid[pos] = sourceGrid[pos];

    for (int dy = -1; dy <= 1; ++dy)
    {
        for (int dx = -1; dx <= 1; ++dx)
        {
            if (dx == 0 && dy == 0) continue;

            int newX = offsets[pos].x + dx;
            int newY = offsets[pos].y + dy;
            if (newX >= 0 && newX < SIZE && newY >= 0 && newY < SIZE)
            {
                int newOffset = newY * SIZE + newX;
                RecursivelyCopy(sourceGrid, destGrid, newOffset);
            }
        }
    }
}

private string GetHash()
{
    var mem = new MemoryStream();
    using (var w = new BinaryWriter(mem))
    {
        for (int y = ymin; y <= ymax; ++y)
        {
            for (int x = xmin; x <= xmax; ++x)
            {
                w.Write(rebuiltStones[y * SIZE + x].ToString("D3"));
            }
        }
    }
    var res = System.Security.Cryptography.MD5.Create().ComputeHash(mem.ToArray());
    var sb = new StringBuilder();
    for (int i = 0; i < res.Length; ++i)
    {
        sb.Append(res[i].ToString("X2"));
    }
    return sb.ToString();
}

private string GetContent()
{
    var sb = new StringBuilder();
    //using (var sw = new StreamWriter(filename, false))
    {
        for (int y = ymin; y <= ymax; ++y)
        {
            for (int x = xmin; x <= xmax; ++x)
            {
                if (x > xmin)
                    sb.Append(" ");
                sb.Append(rebuiltStones[y * SIZE + x].ToString("D3"));
            }
            sb.AppendLine();
        }
    }
    return sb.ToString();
}
private int ComputeBounds()
{
    int countOnes = 0;
    // now we compute the smallest rectangle
    xmin = 1 << 30;
    ymin = 1 << 30;
    xmax = 0;
    ymax = 0;

    for (int i = 0; i < SIZE * SIZE; ++i)
    {
        if (rebuiltStones[i] != 0)
        {
            int x = offsets[i].x;
            int y = offsets[i].y;
            xmin = Math.Min(xmin, x);
            xmax = Math.Max(xmax, x);
            ymin = Math.Min(ymin, y);
            ymax = Math.Max(ymax, y);
            if (rebuiltStones[i] == 1)
                ++countOnes;
        }
    }
    return countOnes;
}

private void PropagateSum(int pos, int stone)
{
    rebuiltStones[pos] = stone;
    if (offsets[pos].x > 0 && offsets[pos].y > 0)
    {
        sums[pos - SIZE - 1] += stone;
    }
    if (offsets[pos].y > 0)
    {
        sums[pos - SIZE] += stone;
    }
    if (offsets[pos].x < SIZE - 1 && offsets[pos].y > 0)
    {
        sums[pos - SIZE + 1] += stone;
    }
    if (offsets[pos].x > 0)
    {
        sums[pos - 1] += stone;
    }
    if (offsets[pos].x < SIZE - 1)
    {
        sums[pos + 1] += stone;
    }
    if (offsets[pos].x > 0 && offsets[pos].y < SIZE - 1)
    {
        sums[pos + SIZE - 1] += stone;
    }
    if (offsets[pos].y < SIZE - 1)
    {
        sums[pos + SIZE] += stone;
    }
    if (offsets[pos].x < SIZE - 1 && offsets[pos].y < SIZE - 1)
    {
        sums[pos + SIZE + 1] += stone;
    }
}
public bool IsRemovable(int pos)
{
    if (offsets[pos].x > 0 && offsets[pos].y > 0)
    {
        if (rebuiltStones[pos - SIZE - 1] > 1)
            return false;
    }
    if (offsets[pos].y > 0)
    {
        if (rebuiltStones[pos - SIZE] > 1)
            return false;
    }
    if (offsets[pos].x < SIZE - 1 && offsets[pos].y > 0)
    {
        if (rebuiltStones[pos - SIZE + 1] > 1)
            return false;
    }
    if (offsets[pos].x > 0)
    {
        if (rebuiltStones[pos - 1] > 1)
            return false;
    }
    if (offsets[pos].x < SIZE - 1)
    {
        if (rebuiltStones[pos + 1] > 1)
            return false;
    }
    if (offsets[pos].x > 0 && offsets[pos].y < SIZE - 1)
    {
        if (rebuiltStones[pos + SIZE - 1] > 1)
            return false;
    }
    if (offsets[pos].y < SIZE - 1)
    {
        if (rebuiltStones[pos + SIZE] > 1)
            return false;
    }
    if (offsets[pos].x < SIZE - 1 && offsets[pos].y < SIZE - 1)
    {
        if (rebuiltStones[pos + SIZE + 1] > 1)
            return false;
    }
    return true;
}
    }
    public class States
{

    private readonly string _prefix;
    private readonly int _beamSize, _min, _max, _order;
    public HashSet<string> hashes;
    public States(string prefix, int order, int beamSize, int min, int max)
    {
        _prefix = prefix;
        _order = order;
        _beamSize = beamSize;
        _min = min;
        _max = max;
    }

    private string GetFileName()
    {
        return _prefix + "_" + _order + "_" + _beamSize + "_" + _min + "_" + _max + ".txt";
    }
    //public bool AlreadyComputed(string hash)
    //{
    //    return loaded.Contains(hash);
    //}

    public int Load()
    {
        int record = 0;
        hashes = new HashSet<string>();
        string f = GetFileName();
        if (File.Exists(f))
        {
            using (var sr = new StreamReader(f))
            {
                while (!sr.EndOfStream)
                {
                    string line = sr.ReadLine();
                    var parts = line.Split(';');
                    hashes.Add(parts[0]);
                    if (parts.Length > 1)
                        record = Math.Max(record, Convert.ToInt32(parts[1]));
                }
            }
        }
        return record;
    }

    public void SaveHash(string hash, int result)
    {
        //loaded.Add(hash);
        using (var sw = new StreamWriter(GetFileName(), true))
        {
            sw.WriteLine("{0};{1}", hash, result);
        }
    }
}
}
