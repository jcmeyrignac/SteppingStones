using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace Collect
{
    /*
     * TODO: faster hash computation -> hash library?
     * 
     * #include "records.h"
     * 
     */
    public class Point
    {
        public int x, y;
    }

    public class Record
    {
        public int value;
        public int count;
        public StreamWriter stream;
        public HashSet<string> hashes;
        public string filename;
    }

    internal static class Collect
    {
        private static readonly string[] rootPaths = new string[] { "d:/SteppingStones/Compute3", "d:/SteppingStones/Compute", "d:/SteppingStones/Compute2" };
        private static Dictionary<int, Record> _records;
        private const int MaxOrder = 32;
        private static int forcedOrder = -1;
        private static int forcedValue = -1;
        private const int SIZE = 129;   // odd valued
        private const int CENTER = ((SIZE / 2) * SIZE) + (SIZE / 2);
        private static int[,] rotations;

        private static int countDuplicates1, countDuplicates2;
        private static void Main(string[] args)
        {
            rotations = new int[7, SIZE * SIZE];
            PrecomputeRotations();
            //throw new Exception("Remove corrupt solutions + rotated/symmetries");
            if (args.Length != 0)
            {
                forcedOrder = Convert.ToInt32(args[0]);
                if (args.Length > 1)
                    forcedValue = Convert.ToInt32(args[1]);
            }

            _records = new Dictionary<int, Record>();
            foreach (var root in rootPaths)
            {
                Recurse(root);
            }

            string output = "stats" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".csv";
            if (forcedOrder >= 0)
                output = "stats-" + forcedOrder + ".csv";
            using (var stats = new StreamWriter(output))
            {
                for (int order = 0; order <= MaxOrder; ++order)
                {
                    if (!_records.ContainsKey(order))
                        continue;
                    stats.WriteLine("{0};{1};{2}", order, _records[order].value, _records[order].count);
                    _records[order].stream.Flush();
                    _records[order].stream.Close();
                    _records[order].stream = null;
                    //int count = 1;
                    //using (var sol = new StreamWriter("soutput" + order.ToString("D2") + "-" + records[order].ToString("D3") + ".txt", false))
                    //{
                    //    foreach (var entry in solutions[order])
                    //    {
                    //        sol.WriteLine("Solution " + count);
                    //        foreach (var lines in entry.Value)
                    //        {
                    //            sol.WriteLine(lines);
                    //        }
                    //        ++count;
                    //    }
                    //}
                }
            }
        }

        private static void Recurse(string folder)
        {
            foreach (var f in Directory.GetFiles(folder, "output*.txt"))
            {
                Extract(f);
            }
            foreach (var f in Directory.GetFiles(folder, "soutput*.txt"))
            {
                Extract(f);
            }
            foreach (var f in Directory.GetDirectories(folder))
            {
                Recurse(f);
            }
        }

        private static void Extract(string filename)
        {
            string f = Path.GetFileNameWithoutExtension(filename);
            f = f.Replace("soutput", "");
            f = f.Replace("output", "");
            var parts = f.Split('-');
            int order = Convert.ToInt32(parts[0]);

            if (forcedOrder >= 0 && order != forcedOrder)
                return;

            if (order > MaxOrder)
                return;
            Console.Write("{0} {1} {2}\r", countDuplicates1, countDuplicates2, filename);
            int record = 0;
            foreach (var c in parts[1])
            {
                if (!char.IsDigit(c))
                    break;
                record = (record * 10) + (c - '0');
            }

            if (forcedValue >= 0 && record != forcedValue)
                return;

            if (!_records.ContainsKey(order))
            {
                var r = new Record();
                r.stream = null;
                r.value = -1;
                r.count = 0;
                r.hashes = null;
                _records.Add(order, r);
            }

            if (_records[order].value > record)
                return;

            if (_records[order].hashes == null)
            {
                _records[order].hashes = new HashSet<string>();
                _records[order].count = 0;
            }
            if (record > _records[order].value)
            {
                Console.WriteLine("{0} {1}", order, record);
                _records[order].value = record;
                if (_records[order].stream != null)
                {
                    _records[order].stream.Close();
                    File.Delete(_records[order].filename);
                }
                _records[order].filename = "soutput" + order.ToString("D2") + "-" + record.ToString("D3") + ".txt";
                _records[order].stream = new StreamWriter(_records[order].filename, false);
                _records[order].hashes = new HashSet<string>();
                _records[order].count = 0;
            }

            var list = new List<string>();
            int countSolutions = 0;
            using (var file = new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                using (var sr = new StreamReader(file))
                {
                    while (!sr.EndOfStream)
                    {
                        string line = sr.ReadLine().Trim();
                        if (string.IsNullOrEmpty(line))
                            continue;
                        if (line.StartsWith("("))
                            continue;

                        if (line.StartsWith("Solution") || line.Contains(","))
                        {
                            if (list.Count != 0)
                            {
                                ++countSolutions;
                                ParseGridAndStore(order, record, list);
                            }
                            list = new List<string>();
                        }
                        else
                        {
                            list.Add(line);
                        }
                    }
                    if (list.Count != 0)
                    {
                        ++countSolutions;
                        ParseGridAndStore(order, record, list);
                    }
                }
            }
            Console.WriteLine("{0}: {1}", filename, countSolutions);
        }

        private static List<string> ComputeGridKey(int[] grid)
        {
            int xmin = 1 << 30;
            int ymin = 1 << 30;
            int xmax = 0;
            int ymax = 0;
            for (int y = 0; y < SIZE; ++y)
            {
                for (int x = 0; x < SIZE; ++x)
                {
                    int c = grid[(y * SIZE) + x];
                    if (c != 0)
                    {
                        xmin = Math.Min(xmin, x);
                        xmax = Math.Max(xmax, x);
                        ymin = Math.Min(ymin, y);
                        ymax = Math.Max(ymax, y);
                    }
                }
            }

            var newList = new List<string>();
            for (int y = ymin; y <= ymax; ++y)
            {
                var sb = new StringBuilder();
                for (int x = xmin; x <= xmax; ++x)
                {
                    int c = grid[(y * SIZE) + x];
                    if (x != xmin)
                        sb.Append(" ");
                    sb.Append(c.ToString("D3"));
                }
                newList.Add(sb.ToString());
            }
            return newList;
        }

        private static void ParseGridAndStore(int order, int record, List<string> list)
        {
            var fastDuplicate = new int[SIZE * SIZE];

            int xmax = 0;
            int ymax = 0;

            var stones = new Dictionary<int, int>();
            var grid = new List<List<int>>();
            var positionStones = new int[record - 1];
            for (int i = 0; i < record - 1; ++i)
                positionStones[i] = -1;
            var positionOnes = new List<int>();
            {
                int y = 0;
                foreach (var line in list)
                {
                    if (line.Contains(","))
                        continue;
                    var cells = line.Split(' ');
                    var ll = new List<int>();
                    int x = 0;
                    foreach (var cell in cells)
                    {
                        if (!Int32.TryParse(cell, out int c))
                        {
                            return;
                        }

                        if (!stones.ContainsKey(c))
                            stones.Add(c, 0);
                        ++stones[c];
                        ll.Add(c);

                        int offset = (y * SIZE) + x;
                        if (c == 1)
                            positionOnes.Add(offset);
                        else if (c > 1)
                            positionStones[c - 2] = offset;
                        fastDuplicate[offset] = c;
                        xmax = Math.Max(xmax, x);
                        ymax = Math.Max(ymax, y);
                        ++x;
                    }
                    grid.Add(ll);
                    ++y;
                }
            }

            // check the solution
            if (!stones.ContainsKey(1))
            {
                Debugger.Break();
                return;
                //Console.WriteLine("Corrupted grid {0} file {1}: missing one", countSolutions, filename);
                //bad = true;
            }
            else if (order >= 6 && stones[1] != order)
            {
                Debugger.Break();
                return;
                //bad = true;
                //Console.WriteLine("Corrupted grid {0} file {1}: incorrect ones {2}/{3}", countSolutions, filename, stones[1], order);
            }

            // check that we have a rectangle
            foreach (var ll in grid)
            {
                if (ll.Count != grid[0].Count)
                {
                    Debugger.Break();
                    return;
                }
            }

            for (int i = 2; i <= record; ++i)
            {
                if (!stones.ContainsKey(i))
                {
                    Debugger.Break();
                    return;
                    //bad = true;
                    //Console.WriteLine("Corrupted grid {0} file {1}: no stone {2}", countSolutions, filename, i);
                }
                else if (stones[i] != 1)
                {
                    Debugger.Break();
                    return;
                    //bad = true;
                    //Console.WriteLine("Corrupted grid {0} file {1}: too many stones {2}/{3}", countSolutions, filename, i, stones[i]);
                }
            }

            // before checking the rotations, we check if the grid already exists in the cache
            string key1 = ComputeKey(ComputeGridKey(fastDuplicate));
            if (_records[order].hashes.Contains(key1))
            {
                ++countDuplicates1;
                return;
            }

            // move the cells to the center !!!
            int moveToCenter = CENTER - positionStones[2];
            for (int st = 0; st < positionOnes.Count; ++st)
            {
                positionOnes[st] += moveToCenter;
            }
            for (int st = 0; st < record - 1; ++st)
            {
                positionStones[st] += moveToCenter;
            }

            if (positionStones[2] != CENTER)
                Debugger.Break();

            var bestKey = BuildHash(positionOnes, positionStones);

            //// we try the 7 different rotations, and keep the best one
            for (int rot = 0; rot < 7; ++rot)
            {

                var newOnes = new List<int>();
                for (int st = 0; st < positionOnes.Count; ++st)
                {
                    int posOrg = positionOnes[st];
                    newOnes.Add(rotations[rot, posOrg]);
                }
                newOnes.Sort();
                var newStones = new int[record - 1];
                for (int st = 0; st < record - 1; ++st)
                {
                    newStones[st] = rotations[rot, positionStones[st]];
                }
                var key2 = BuildHash(newOnes, newStones);
                if (string.Compare(key2, bestKey) < 0)
                {
                    bestKey = key2;
                    positionStones = newStones;
                    positionOnes = newOnes;
                }
            }

            // now we save the best solution!
            var gridToSave = new int[SIZE * SIZE];

            for (int st = 0; st < positionOnes.Count; ++st)
            {
                gridToSave[positionOnes[st]] = 1;
            }
            for (int st = 0; st <= record - 2; ++st)
            {
                gridToSave[positionStones[st]] = st + 2;
            }

            // count around ones
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

                        if (newP >= 0 && newP < SIZE * SIZE && gridToSave[newP] > 1)
                            ++countCellsAroundOnes;
                    }
                }
            }

            var newList = ComputeGridKey(gridToSave);
            string key = ComputeKey(newList);
            // store the grid
            if (!_records[order].hashes.Contains(key))
            {
                _records[order].hashes.Add(key);
                ++_records[order].count;
                _records[order].stream.WriteLine("Area {0}, Solution {1}, AroundOnes {2}", xmax * ymax, _records[order].count, countCellsAroundOnes);
                foreach (var l in newList)
                {
                    _records[order].stream.WriteLine(l);
                }
            }
            else
                ++countDuplicates2;
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

        private static string ComputeKey(List<string> list)
        {
            var sb = new StringBuilder();
            foreach (var line in list)
            {
                if (line.StartsWith("("))
                    break;
                sb.Append(line.Replace(" ", ""));
            }
            return sb.ToString();
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
                        int offset = (y * SIZE) + x;
                        source.x = x;
                        source.y = y;
                        var p = Rotate(source, rot);
                        rotations[rot, offset] = (p.y * SIZE) + p.x;
                    }
                }
            }
        }
    }
}
