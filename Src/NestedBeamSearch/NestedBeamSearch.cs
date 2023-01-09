using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection.Emit;
using System.Text;
using System.Threading;

namespace NestedBeamSearch
{
    /*

     * 1) load a list of positions
     * 2) for each position, perform a beamsearch
     * 3) for each position, store the reached depth
     * 4) when the computation is over, keep the X best positions and add one stone
     * 5) goto 1)
     *
     * Note that BeamsearchCompressed MUST return the reached level, in order that the algorithm works
     *
     * Done:
        + add Parallel search
        + explore upto depth level+K (not levelMax)
        + automatic compiling
        + add Borders
        + Start at 2
        + add 2 ones
        + why do I have a different counting on level 4?
        + start with a set of solutions (e.g. 8-78)
        + Task -> Thread ?
        + use beam.c / threads=1
        + optimize PlayOne with neighbors in BeamCompressed.c
     */
    public class Point
    {
        public int x, y;
    }

    public class Position
    {
        public int[] cells;
    }

    internal static class NestedBeamSearch
    {
        const bool AllowAjdacentOnes = false;               // allow adjacent ones
        const bool AllowAjdacentOnesAtTheBeginning = true;  // allow adjacent ones at the beginning
        const bool AllowTwoOnes = false;                    // allow placing two ones at a time
        const int maxThreads = 12;                          // maximum number of threads
        const bool generateSubOptimalGrids = true;          // heuristic to avoid generating suboptimal grids

        private static bool useBeamCompressed = false;     // autodetect true=use BeamCompressed.c, false=use Beam.c
        private const int SIZE = 129;                       // this value must be odd
        private const int CENTER = SIZE / 2 * SIZE + SIZE / 2;
        private static int[,] rotations;
        const string workFile = "input.txt";
        private static int beamSize;
        private static Dictionary<string, Position> savedPositions;
        private static int[] directions;
        const int maximumStones = 300;
        private static readonly object saveLock = new object();
        private static ConcurrentQueue<KeyValuePair<int, Position>> _queue;
        private const string compilerPath = @"C:\Program Files\LLVM\bin\clang-cl.exe";
        private const string arguments = "/arch:AVX2 /Fe{1} /fp:fast /Gr /GS- /MT /O2 /Oi /Ot /W4 /Fa{3} {0} {2}.c";

        private static Dictionary<int, int> _results;
        private static string _resultsFile;
        private static int _level;
        private static int _maxOrder;
        private static int _totalPositions;
        private static volatile int _Record;
        private static volatile int countRecords;
        private static List<Point> patternOffsets;
        private static int currentNumberOfOnes;

        static void Main(string[] args)
        {
            if (args.Length < 4)    
            {
                Console.WriteLine("MCTS MaxOrder BeamSize Start MCTSsize");
                Console.WriteLine("Processors count: {0}", Environment.ProcessorCount);
                return;
            }

            useBeamCompressed = File.Exists("BeamCompressed.c");

            directions = new int[8] { -SIZE - 1, -SIZE, -SIZE + 1, -1, 1, SIZE - 1, SIZE, SIZE + 1 };
            int maxOrder = Convert.ToInt32(args[0]);
            beamSize = Convert.ToInt32(args[1]);
            int level = Convert.ToInt32(args[2]);
            int keepPositions = Convert.ToInt32(args[3]);

            int continuousSteps = 0;
            if (args.Length > 4)
                continuousSteps = Convert.ToInt32(args[4]); 
            //double coefBeam = 1;
            //double coefKeep = 1;
            //if (args.Length > 4)
            //{
            //    int rec = Convert.ToInt32(args[4]);
            //    coefBeam = Math.Pow(100.0, 1.0 / rec);
            //    coefKeep = Math.Pow(10.0, 1.0 / rec);
            //    Console.WriteLine("CoefBeam={0} CoefKeep={1}", coefBeam, coefKeep);
            //}

            LoadPattern("pattern.h");           // the pattern we'll use, if the file does not exist, no pattern is applied
            BuildExecutable(maxOrder);

            rotations = new int[7, SIZE * SIZE];
            PrecomputeRotations();

            if (continuousSteps == 0)
                ExecuteOneRound(maxOrder, level, keepPositions, false);

            else
            {
                for (; ; )
                {
                    ExecuteOneRound(maxOrder, level, keepPositions, true);
                    level -= continuousSteps;
                    if (level < 6)
                        break;
                }
            }
        }

        private static void ExecuteOneRound(int maxOrder, int level, int keepPositions, bool continuous)
        {
            var destinationFolder = "Save" + level.ToString("D3");
            // already computed?
            if (!continuous || !Directory.Exists(destinationFolder))
            {
                currentNumberOfOnes = 0;
                if (level == 2)
                    GenerateInitialPositions(level);
                else
                    LoadInitialGrids(level);
                while (level < maximumStones)
                {
                    // we move the following files into a new folder
                    Console.WriteLine("Level {0}, maxOrder={1}, beamSize={2}, keep={3}", level, maxOrder, beamSize, keepPositions);
                    int count = Compute(level, keepPositions, maxOrder);
                    if (count == 0)
                        break;
                    ++level;
                    //beamSize = (int)(beamSize * coefBeam);
                    //keepPositions = (int)(keepPositions * coefKeep);
                }
                Directory.CreateDirectory(destinationFolder);
                MoveFiles(destinationFolder, "Positions*.txt");
                MoveFiles(destinationFolder, "Computed*.txt");
                MoveFiles(destinationFolder, "progress*.txt");
            }
        }

        private static void MoveFiles(string destinationFolder, string pattern)
        {
            foreach (var f in Directory.GetFiles(Directory.GetCurrentDirectory(), pattern))
            {
                File.Move(f, Path.Combine(destinationFolder, Path.GetFileName(f)));
            }
        }


        private static Point Rotate(Point source, int rotation)
        {
            var p = new Point();
            switch (rotation)
            {
                case 0:
                    p.x = SIZE - 1 - source.x;
                    p.y = source.y;
                    break;
                case 1:
                    p.x = source.x;
                    p.y = SIZE - 1 - source.y;
                    break;
                case 2:
                    p.x = SIZE - 1 - source.x;
                    p.y = SIZE - 1 - source.y;
                    break;
                case 3:
                    p.x = source.y;
                    p.y = source.x;
                    break;
                case 4:
                    p.x = SIZE - 1 - source.y;
                    p.y = source.x;
                    break;
                case 5:
                    p.x = source.y;
                    p.y = SIZE - 1 - source.x;
                    break;
                case 6:
                    p.x = SIZE - 1 - source.y;
                    p.y = SIZE - 1 - source.x;
                    break;
                default:
                    throw new Exception("Unknown rotation");
            }
            return p;
        }

        private static void PrecomputeRotations()
        {
            var source = new Point();
            for (int y = 0; y < SIZE; ++y)
            {
                for (int x = 0; x < SIZE; ++x)
                {
                    for (int rot = 0; rot < 7; ++rot)
                    {
                        int offset = y * SIZE + x;
                        source.x = x;
                        source.y = y;
                        var p = Rotate(source, rot);
                        rotations[rot, offset] = p.y * SIZE + p.x;
                    }
                }
            }
        }

        private static string GetPositionsFile(int level)
        {
            return string.Format("Positions{0:D3}.txt", level);
        }
        private static string GetResultsFile(int level)
        {
            return string.Format("Computed{0:D3}.txt", level);
        }

        private static volatile int seconds = 0;

        private static void CompareToRecord(int value)
        {
            if (value > _Record)
            {
                _Record = value;
                countRecords = 1;
            }
            else if (value == _Record)
            {
                ++countRecords;
            }
        }

        private static void ThreadWithState(object o)
        {
            int slot = (int)o;
            KeyValuePair<int, Position> ent;
            while (_queue.TryDequeue(out ent))
            {
                if (_results.ContainsKey(ent.Key))
                {
                    CompareToRecord(_results[ent.Key]);
                }
                else
                {
                    string folder = ContextFolder(slot);
                    var d = DateTime.Now.Second;
                    if (d != seconds)
                    {
                        seconds = d;
                        Console.Write("{0}/{1}/{2}/{3}({4}) \r", ent.Key, _totalPositions, slot, _Record, countRecords);
                    }
                    Directory.CreateDirectory(folder);

                    int countOnes = CountOnes(ent.Value);
                    int res = 0;
                    // skip solutions with too much ones
                    if (countOnes <= _maxOrder)
                    {
                        using (var gridFile = new StreamWriter(Path.Combine(folder, workFile)))
                        {
                            SavePosition(gridFile, ent.Value, 0);
                        }
                        res = ExecuteBeamSearch(_level, folder, _maxOrder, slot);
                    }
                    lock (saveLock)
                    {
                        CompareToRecord(res);
                        _results.Add(ent.Key, res);
                        using (var sw = new StreamWriter(_resultsFile, true))
                        {
                            sw.WriteLine("{0},{1}", res, ent.Key);
                        }
                    }
                }
            }
        }

        private static int Compute(int level, int keepPositions, int maxOrder)
        {
            // if the computation has already been done, we don't recompute!
            string outputFile = GetPositionsFile(level + 1);
            if (File.Exists(outputFile))
                return 1;

            string inputFile = GetPositionsFile(level);
            if (!File.Exists(inputFile))
                throw new Exception("No input");
            _resultsFile = GetResultsFile(level);
            _level = level;
            _Record = 0;
            countRecords = 0;

            var positions = LoadPositions(level, inputFile);
            _results = LoadResults(_resultsFile);

            if (maxThreads > 1)
            {
                _totalPositions = positions.Count;
                _maxOrder = maxOrder;
                _queue = new ConcurrentQueue<KeyValuePair<int, Position>>(positions);
                var tasks = new Thread[maxThreads];
                for (int i = 0; i < maxThreads; ++i)
                {
                    tasks[i] = new Thread(ThreadWithState);
                    tasks[i].Start(i);
                }
                for (int i = 0; i < maxThreads; ++i)
                {
                    tasks[i].Join();
                    tasks[i] = null;
                }
            }
            else
            {
                for (int i = 0; i < positions.Count; ++i)
                {
                    if (_results.ContainsKey(i))
                    {
                        CompareToRecord(_results[i]);
                    }
                    else
                    {
                        // save the position and run BeamSearchCompressed31.exe
                        // retrieve the number of positions
                        var d = DateTime.Now.Second;
                        if (d != seconds)
                        {
                            seconds = d;
                            Console.Write("{0}/{1}/{2}({3}) \r", i, positions.Count, _Record, countRecords);
                        }
                        int countOnes = CountOnes(positions[i]);
                        int res = 0;
                        if (countOnes <= _maxOrder)
                        {
                            using (var gridFile = new StreamWriter(workFile))
                            {
                                SavePosition(gridFile, positions[i], 0);
                            }
                            res = ExecuteBeamSearch(level, Directory.GetCurrentDirectory(), maxOrder, 0);
                        }
                        CompareToRecord(res);
                        _results.Add(i, res);
                        using (var sw = new StreamWriter(_resultsFile, true))
                        {
                            sw.WriteLine("{0},{1}", res, i);
                        }
                    }
                }
            }

            // when all the positions have been computed, we sort them by score, keep the best ones and then add one new cell
            var dict = new SortedList<int, int>();
            for (int i = 0; i < positions.Count; ++i)
            {
                dict.Add((maximumStones - _results[i]) * 1000000 + i, i);
            }

            currentNumberOfOnes = 0;
            savedPositions = new Dictionary<string, Position>();
            int minScore = int.MaxValue;
            int maxScore = int.MinValue;
            var scores = new SortedList<int, int>();
            for (int i = 0; i < dict.Count; ++i)
            {
                if (savedPositions.Count >= keepPositions)
                    break;
                int p = dict.Values[i];

                int score = _results[p];
                if (!scores.ContainsKey(score))
                    scores.Add(score, 0);
                ++scores[score];

                minScore = Math.Min(minScore, score);
                maxScore = Math.Max(maxScore, score);
                //Console.WriteLine("Grid {0} {1} {2}", p, _results[p], dict.Keys[i]);
                PlayAllMoves(positions[p], level + 1, maxOrder);
            }
            Console.WriteLine("Saving {0} positions with scores from {1} to {2}", savedPositions.Count, maxScore, minScore);

            using (var o = new StreamWriter("progress.txt", true))
            {
                var sb = new StringBuilder();
                sb.Append(level).Append(": ").Append(maxScore).Append("-").Append(minScore).Append(" ").Append(savedPositions.Count);
                for (int i = scores.Count - 1; i >= 0; --i)
                {
                    sb.Append(" ").Append(scores.Keys[i]).Append("(").Append(scores.Values[i]).Append(")");
                }
                o.WriteLine(sb);
            }

            using (var outf = new StreamWriter(outputFile))
            {
                int count = 0;
                foreach (var pos in savedPositions)
                {
                    ++count;
                    SavePosition(outf, pos.Value, count);
                }
            }
            return savedPositions.Count;
        }

        private static int ExecuteBeamSearch(int level, string folder, int maxOrder, int slot)
        {
            var sb = new StringBuilder();
            sb.Append(beamSize);
            sb.Append(" ").Append(workFile);
            sb.Append(" ").Append(level);
            sb.Append(" ").Append(maximumStones);

            //Console.WriteLine(sb.ToString());
            var process = new ProcessStartInfo(Path.Combine(Directory.GetCurrentDirectory(), "Beam" + maxOrder + ".exe"))
            {
                Arguments = sb.ToString(),
                WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = folder
            };
            var proc = Process.Start(process);
            //if (proc.Threads.Count != 0)
            //{
            //    var thread = proc.Threads[0];
            //    thread.IdealProcessor = slot;
            //    thread.ProcessorAffinity = (IntPtr)(1L << slot);
            //}
            proc.WaitForExit();
            return proc.ExitCode;
        }

        private static Dictionary<int, int> LoadResults(string filename)
        {
            var liste = new Dictionary<int, int>();
            if (File.Exists(filename))
            {
                using (var sr = new StreamReader(filename))
                {
                    int count = 0;
                    while (!sr.EndOfStream)
                    {
                        string line = sr.ReadLine();
                        var parts = line.Split(',');
                        if (parts.Length == 1)
                        {
                            // old format
                            liste.Add(count, Convert.ToInt32(parts[0]));
                        }
                        else
                        {
                            liste.Add(Convert.ToInt32(parts[1]), Convert.ToInt32(parts[0]));
                        }
                        ++count;
                    }
                }
            }
            return liste;
        }

        private static Dictionary<int, Position> LoadPositions(int level, string inputFile)
        {
            var liste = new Dictionary<int, Position>();
            int count = 0;
            var grid = new List<string>();
            using (var sr = new StreamReader(inputFile))
            {
                while (!sr.EndOfStream)
                {
                    string line = sr.ReadLine().Trim();
                    line = line.Replace("\u001a", "");
                    if (string.IsNullOrEmpty(line))
                        continue;
                    if (line.StartsWith("Solution") || line.Contains(",") || line.StartsWith("("))
                    {
                        if (grid.Count != 0)
                        {
                            var pos = ParsePosition(level, grid);
                            liste.Add(count, pos);
                            ++count;
                        }
                        grid = new List<string>();
                    }
                    else
                    {
                        grid.Add(line);
                    }
                }
                if (grid.Count != 0)
                {
                    var pos = ParsePosition(level, grid);
                    liste.Add(count, pos);
                    ++count;
                }
            }
            return liste;
        }

        private static Position ParsePosition(int level, List<string> list)
        {
            var stones = new Dictionary<int, int>();
            var grid = new List<List<int>>();
            var positionStones = new Dictionary<int, int>();
            var positionOnes = new List<int>();
            {
                int y = 0;
                foreach (var line in list)
                {
                    var cells = line.Split(' ');
                    var ll = new List<int>();
                    int x = 0;
                    foreach (var cell in cells)
                    {
                        int c;
                        if (!int.TryParse(cell, out c))
                        {
                            throw new Exception("Bad cell");
                        }
                        if (c > level)
                            throw new Exception("Out of stones " + c);

                        if (!stones.ContainsKey(c))
                            stones.Add(c, 0);
                        ++stones[c];
                        ll.Add(c);

                        int offset = y * SIZE + x;
                        if (c == 1)
                            positionOnes.Add(offset);
                        else if (c > 1)
                            positionStones.Add(c, offset);
                        ++x;
                    }
                    grid.Add(ll);
                    ++y;
                }
            }

            // check the solution
            if (!stones.ContainsKey(1))
            {
                throw new Exception("No one");
            }

            // check that we have a rectangle
            foreach (var ll in grid)
            {
                if (ll.Count != grid[0].Count)
                {
                    throw new Exception("Not a rectangle");
                }
            }

            for (int i = 2; i <= level; ++i)
            {
                if (!stones.ContainsKey(i))
                {
                    throw new Exception("Missing stone" + i);
                }
                else if (stones[i] != 1)
                {
                    throw new Exception("Too many stones " + i);
                }
            }

            int offsetCenter = CENTER - positionStones[2];

            // now we save the best solution!
            var pos = new Position
            {
                cells = new int[SIZE * SIZE]
            };
            SetBorders(pos);

            for (int st = 0; st < positionOnes.Count; ++st)
            {
                int c = positionOnes[st] + offsetCenter;
                //if (!CanWePlaceAOneHere(pos, c))
                //{
                //    Debugger.Break();
                //    return null;
                //}
                pos.cells[c] = 1;
            }
            for (int st = 2; st <= level; ++st)
            {
                pos.cells[positionStones[st] + offsetCenter] = st;
            }
            return pos;
        }

        private static void SavePosition(StreamWriter write, Position pos, int count)
        {
            int xmin = 1 << 30;
            int ymin = 1 << 30;
            int xmax = 0;
            int ymax = 0;
            int nbOnes = 0;
            int maxStone = 0;
            for (int y = 0; y < SIZE; ++y)
            {
                for (int x = 0; x < SIZE; ++x)
                {
                    int c = pos.cells[y * SIZE + x];
                    if (c > 0)
                    {
                        maxStone = Math.Max(maxStone, c);
                        if (c == 1) ++nbOnes;
                        xmin = Math.Min(xmin, x);
                        xmax = Math.Max(xmax, x);
                        ymin = Math.Min(ymin, y);
                        ymax = Math.Max(ymax, y);
                    }
                }
            }

            if (count != 0)
                write.WriteLine("Solution {0} {1}/{2}", count, nbOnes, maxStone);
            for (int y = ymin; y <= ymax; ++y)
            {
                var sb = new StringBuilder();
                for (int x = xmin; x <= xmax; ++x)
                {
                    int c = pos.cells[y * SIZE + x];
                    if (x != xmin)
                        sb.Append(" ");
                    sb.Append(c.ToString("D3"));
                }
                write.WriteLine(sb.ToString());
            }
        }

        private static int CountOnes(Position pos)
        {
            int count = 0;
            for (int i = 0; i < SIZE * SIZE; ++i)
                if (pos.cells[i] == 1)
                    ++count;
            return count;
        }


        private static void PlayAllMoves(Position pos, int stone, int maxOrder)
        {
            var sums = new int[SIZE * SIZE];
            int nbOnes = 0;
            for (int i = 0; i < SIZE * SIZE; ++i)
            {
                int v = pos.cells[i];
                if (v > 0)
                {
                    for (int d = 0; d < 8; ++d)
                        sums[i + directions[d]] += v;
                    if (v == 1)
                        ++nbOnes;
                }
            }

            bool found = false;

            for (int i = 0; i < SIZE * SIZE; ++i)
            {
                if (pos.cells[i] != 0)
                    continue;

                if (sums[i] == stone)
                {
                    pos.cells[i] = stone;
                    StorePosition(pos, stone);
                    pos.cells[i] = 0;
                }
            }

            if (found && !generateSubOptimalGrids)
                return;

            for (int i = 0; i < SIZE * SIZE; ++i)
            {
                if (pos.cells[i] != 0)
                    continue;

                if (sums[i] == stone - 1 && nbOnes + 1 <= maxOrder)
                {
                    // we search a place to put a "one"
                    for (int dd1 = 0; dd1 < 8; ++dd1)
                    {
                        int newCell = i + directions[dd1];
                        if (CanWePlaceAOneHere(pos, newCell) && IsPossibleOne(pos, newCell))
                        {
                            pos.cells[newCell] = 1;
                            pos.cells[i] = stone;
                            StorePosition(pos, stone);
                            pos.cells[newCell] = 0;
                            pos.cells[i] = 0;
                        }
                    }
                }
                else if (AllowTwoOnes && sums[i] == stone - 2 && nbOnes + 2 <= maxOrder)
                {
                    // we search a place to put a "one"
                    for (int dd1 = 0; dd1 < 8; ++dd1)
                    {
                        int newCell1 = directions[dd1] + i;
                        if (CanWePlaceAOneHere(pos, newCell1) && IsPossibleOne(pos, newCell1))
                        {
                            pos.cells[newCell1] = 1;
                            for (int dd2 = dd1 + 1; dd2 < 8; ++dd2)
                            {
                                int newCell2 = directions[dd2];
                                if (CanWePlaceAOneHere(pos, newCell2))
                                {
                                    pos.cells[newCell2] = 1;
                                    pos.cells[i] = stone;
                                    StorePosition(pos, stone);
                                    pos.cells[newCell2] = 0;
                                    pos.cells[i] = 0;
                                }
                            }
                            pos.cells[newCell1] = 0;
                        }
                    }
                }
            }
        }

        private static bool CanWePlaceAOneHere(Position pos, int n)
        {
            if (pos.cells[n] != 0)
                return false;

            int minCell = AllowAjdacentOnes ? 1 : 0;
            for (int dd2 = 0; dd2 < 8; ++dd2)
            {
                int dr2 = directions[dd2];
                if (pos.cells[n + dr2] > minCell)
                {
                    return false;
                }
            }
            return true;
        }

        private static bool IsPossibleOne(Position pos, int n)
        {
            // we check that the pattern on the ones
            if (patternOffsets.Count != 0)
            {
                int x = n % SIZE;
                int y = n / SIZE;
                foreach (var p in patternOffsets)
                {
                    int dx = p.x;
                    int dy = p.y;
                    int x2 = x + dx;
                    int y2 = y + dy;
                    if (x2 >= 0 && x2 < SIZE && y2 >= 0 && y2 < SIZE && pos.cells[y2 * SIZE + x2] == 1)
                    {
                        return false;
                    }
                    x2 = x - dx;
                    y2 = y + dy;
                    if (x2 >= 0 && x2 < SIZE && y2 >= 0 && y2 < SIZE && pos.cells[y2 * SIZE + x2] == 1)
                    {
                        return false;
                    }
                    x2 = x + dx;
                    y2 = y - dy;
                    if (x2 >= 0 && x2 < SIZE && y2 >= 0 && y2 < SIZE && pos.cells[y2 * SIZE + x2] == 1)
                    {
                        return false;
                    }
                    x2 = x - dx;
                    y2 = y - dy;
                    if (x2 >= 0 && x2 < SIZE && y2 >= 0 && y2 < SIZE && pos.cells[y2 * SIZE + x2] == 1)
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        private static void StorePosition(Position pos, int level)
        {
            var positionStones = new int[level - 1];
            var positionOnes = new List<int>();

            for (int o = 0; o < SIZE * SIZE; ++o)
            {
                int c = pos.cells[o];
                if (c > 0)
                {
                    switch (c)
                    {
                        case 1:
                            positionOnes.Add(o);
                            break;
                        default:
                            if (c > level)
                                throw new Exception("Too large");
                            positionStones[c - 2] = o;
                            break;
                    }
                }
            }
            if (currentNumberOfOnes == 0)
            {
                currentNumberOfOnes = positionOnes.Count;
                Console.WriteLine("Record={0}", currentNumberOfOnes);
            }
            else if (positionOnes.Count > currentNumberOfOnes + 1)
            {
                return;
            }
            else if (positionOnes.Count < currentNumberOfOnes)
            {
                currentNumberOfOnes = positionOnes.Count;
                Console.WriteLine("Record={0}", currentNumberOfOnes);
            }

            if (pos.cells[CENTER] != 2)
                throw new Exception("2 not at the center");

            var bestOnes = new List<int>(positionOnes);
            int[] bestStones = (int[])positionStones.Clone();

            var bestKey = BuildHash(positionOnes, positionStones);
            //var originalKey = bestKey;

            // we try the 7 different rotations, and keep the smallest canonicalization
            for (int rot = 0; rot < 7; ++rot)
            {
                var newOnes = new List<int>();
                for (int st = 0; st < positionOnes.Count; ++st)
                {
                    int posOrg = positionOnes[st];
                    newOnes.Add(rotations[rot, posOrg]);
                }
                newOnes.Sort();
                var newStones = new int[level - 1];
                for (int st = 0; st < level - 1; ++st)
                {
                    newStones[st] = rotations[rot, positionStones[st]];
                }
                var key2 = BuildHash(newOnes, newStones);
                if (string.Compare(key2, bestKey) < 0)
                {
                    //Console.WriteLine(rot);
                    //Console.WriteLine(key2);
                    //Console.WriteLine(bestKey);
                    bestKey = key2;
                    bestStones = newStones;
                    bestOnes = newOnes;
                }
            }

            var k = BuildHash(bestOnes, bestStones);
            if (k != bestKey)
                throw new Exception("Diff key");


            if (!savedPositions.ContainsKey(k))
            {
                // now we save the best solution!
                var newPos = new Position
                {
                    cells = new int[SIZE * SIZE]
                };
                SetBorders(newPos);

                for (int st = 0; st < bestOnes.Count; ++st)
                {
                    newPos.cells[bestOnes[st]] = 1;
                }
                for (int st = 0; st < level - 1; ++st)
                {
                    newPos.cells[bestStones[st]] = st + 2;
                }

                //Console.WriteLine(k);
                savedPositions.Add(k, newPos);
            }
            //else
            //{
            //    // DEBUG
            //    using (var outf = new StreamWriter("duplicate" + level + ".txt", true))
            //    {
            //        outf.WriteLine("Hash={0}", originalKey);
            //        SavePosition(outf, pos);
            //        outf.WriteLine("Duplicate of hash={0}", bestKey);
            //        SavePosition(outf, newPos);
            //    }
            //}
        }

        private static string BuildHash(List<int> positionOnes, int[] positionStones)
        {
            var sb = new StringBuilder();
            for (int st = 0; st < positionOnes.Count; ++st)
            {
                sb.Append('.').Append(positionOnes[st]);
            }
            for (int st = 0; st < positionStones.Length; ++st)
            {
                sb.Append('_').Append(positionStones[st]);
            }
            return sb.ToString();
        }

        private static string ContextFolder(int thread)
        {
            return string.Format("Run{0:D5}", thread);
        }

        private static void BuildExecutable(int maxOrder)
        {
            string output = string.Format("Beam{0}.exe", maxOrder);
            if (File.Exists(output))
                return;
            string outputAsm = string.Format("Beam{0}.asm", maxOrder);

            var sb = new StringBuilder();
            sb.Append(" -D NN=").Append(maxOrder);
            sb.Append(" -D USE_RANDOMIZED_SCORING=").Append(0);
            sb.Append(" -D USE_PATTERN=").Append(patternOffsets.Count == 0 ? 0 : 1);
            string args = string.Format(arguments, sb.ToString(), output, useBeamCompressed ? "BeamCompressed" : "Beam", outputAsm);
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

        private static void SetBorders(Position pos)
        {
            for (int n = 0; n < SIZE; ++n)
            {
                pos.cells[n] = -1;
                pos.cells[n * SIZE] = -1;
                pos.cells[n * SIZE + SIZE - 1] = -1;
                pos.cells[SIZE * SIZE - SIZE + n] = -1;
            }
        }

        private static void GenerateInitialPositions(int level)
        {
            string outputFile = GetPositionsFile(level);
            if (File.Exists(outputFile))
                return;

            savedPositions = new Dictionary<string, Position>();
            var pos = new Position
            {
                cells = new int[SIZE * SIZE]
            };
            SetBorders(pos);

            pos.cells[CENTER] = 2;
            for (int d1 = 0; d1 < 8; ++d1)
            {
                pos.cells[CENTER + directions[d1]] = 1;
                for (int d2 = d1 + 1; d2 < 8; ++d2)
                {
                    int newCell = CENTER + directions[d2];
                    if (AllowAjdacentOnesAtTheBeginning || CanWePlaceAOneHere(pos, newCell))
                    {
                        pos.cells[newCell] = 1;
                        StorePosition(pos, 2);
                        pos.cells[newCell] = 0;
                    }
                }
                pos.cells[CENTER + directions[d1]] = 0;
            }

            Console.WriteLine("Generated {0} positions", savedPositions.Count);
            using (var outf = new StreamWriter(outputFile))
            {
                int count = 0;
                foreach (var pos2 in savedPositions)
                {
                    ++count;
                    SavePosition(outf, pos2.Value, count);
                }
            }
        }

        private static void LoadInitialGrids(int level)
        {
            string outputFile = GetPositionsFile(level);
            if (File.Exists(outputFile))
                return;

            savedPositions = new Dictionary<string, Position>();
            ReduceGrid("initial.txt", level);
            foreach (var ff in Directory.GetFiles(Directory.GetCurrentDirectory(), "soutput*.txt"))
            {
                ReduceGrid(ff, level);
            }
            Console.WriteLine("Generated {0} positions", savedPositions.Count);
            if (savedPositions.Count == 0)
                throw new Exception("No position has been found");

            using (var outf = new StreamWriter(outputFile))
            {
                int count = 0;
                foreach (var pos2 in savedPositions)
                {
                    ++count;
                    SavePosition(outf, pos2.Value, count);
                }
            }
        }

        private static void ReduceGrid(string inputFile, int level)
        {
            if (!File.Exists(inputFile))
                return;
            Console.Write(inputFile + "\r");
            // we load the solutions, and we reduce the grids if possible
            var grid = new List<string>();
            using (var sr = new StreamReader(inputFile))
            {
                while (!sr.EndOfStream)
                {
                    string line = sr.ReadLine().Trim();
                    line = line.Replace("\u001a", "");
                    if (string.IsNullOrEmpty(line))
                        continue;
                    if (line.StartsWith("Solution") || line.Contains(",") || line.StartsWith("("))
                    {
                        if (grid.Count != 0)
                        {
                            ReducePosition(level, grid);
                        }
                        grid = new List<string>();
                    }
                    else
                    {
                        grid.Add(line);
                    }
                }
                if (grid.Count != 0)
                {
                    ReducePosition(level, grid);
                }
            }
        }

        static void ReducePosition(int level, List<string> grid)
        {
            var stonesPositions = new Dictionary<int, int>();
            var onePositions = new HashSet<int>();
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
            if (!stonesPositions.ContainsKey(level))
                return;

            // now, we rebuild the grid, by placing stones starting with 2
            var sums = new int[SIZE * SIZE];
            var newPos = new Position
            {
                cells = new int[SIZE * SIZE]
            };
            SetBorders(newPos);

            int diff = CENTER - stonesPositions[2];

            for (int stone = 2; stone <= level; ++stone)
            {
                int pos = stonesPositions[stone] + diff;
                if (sums[pos] != stone)
                {
                    // we search all the ones around this cell, and copy them
                    for (int dir = 0; dir < 8; ++dir)
                    {
                        int o = pos + directions[dir];
                        if (newPos.cells[o] == 0 && onePositions.Contains(o - diff))
                        {
                            PropagateSum(o, 1, newPos, sums);
                        }
                    }
                }
                if (sums[pos] != stone)
                    throw new Exception("Sum error");
                PropagateSum(pos, stone, newPos, sums);
            }
            StorePosition(newPos, level);
        }

        static void PropagateSum(int pos, int stone, Position newPos, int[] sums)
        {
            newPos.cells[pos] = stone;
            for (int dir = 0; dir < 8; ++dir)
            {
                int d = directions[dir];
                if (newPos.cells[pos + d] == 0)
                    sums[pos + d] += stone;
            }
        }

        static void LoadPattern(string filename)
        {
            patternOffsets = new List<Point>();
            if (File.Exists(filename))
            {
                var content = File.ReadAllText(filename);
                content = content.Replace("\r", "").Replace("\n", "").Replace(" ", "");
                int pos1 = content.IndexOf("{");
                int pos2 = content.IndexOf("}");
                content = content.Substring(pos1 + 1, pos2 - pos1 - 1);
                var parts = content.Split(',');
                if ((parts.Length % 2) != 0)
                    throw new Exception("Missing number in patterns");
                for (int i = 0; i < parts.Length; i += 2)
                {
                    var p = new Point();
                    p.x = Convert.ToInt32(parts[i]);
                    p.y = Convert.ToInt32(parts[i + 1]);
                    patternOffsets.Add(p);
                }
            }
        }

    }
}
