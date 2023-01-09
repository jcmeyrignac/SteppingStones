using System;
using System.Collections;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Policy;
using System.Text;
using System.Threading.Tasks;

namespace DetectPattern
{
    internal class DetectPattern
    {
        const int SIZE = 128;
        const int PATTERN_SIZE = 23;    // total size
        static HashSet<string> localHash;
        static HashSet<string> localHashPattern;
        static SortedDictionary<int, HashSet<string>> globalHash;
        static void Main(string[] args)
        {
            globalHash = new SortedDictionary<int, HashSet<string>>();
            foreach (var f in Directory.GetFiles(Directory.GetCurrentDirectory(), "soutput*.txt"))
            {
                LoadSolutions(f);
            }
            foreach (var ent in globalHash)
            {
                int count = 0;
                foreach (var pat in ent.Value)
                {
                    ++count;
                    using (var patrn = new StreamWriter(String.Format("pattern{0}_{1}.h", ent.Key, count)))
                    {
                        patrn.Write(pat);
                    }
                }

            }
        }
        static void LoadSolutions(string filename)
        {
            Console.WriteLine(filename);
            localHash = new HashSet<string>();
            localHashPattern = new HashSet<string>();

            var parts = Path.GetFileNameWithoutExtension(filename).Split('-');
            int order = Convert.ToInt32(parts[0].Replace("soutput", ""));

            using (var sw = new StreamWriter("InversePattern" + order.ToString("D2") + ".txt"))
            {

                using (var sw2 = new StreamWriter("BitPattern" + order.ToString("D2") + ".txt"))
                {
                    var grid = new List<string>();
                    using (var sr = new StreamReader(filename, false))
                    {
                        while (!sr.EndOfStream)
                        {
                            string line = sr.ReadLine().Trim();
                            if (line.StartsWith("Solution") || line.Contains(","))
                            {
                                if (grid.Count != 0)
                                {
                                    Parse(grid, sw);
                                    ParsePattern(grid, sw2);
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
                        Parse(grid, sw);
                        ParsePattern(grid, sw2);
                    }
                }
            }
        }



        static void ClearStone(int[] stones, int offset, int dx, int dy, List<int> onePositions)
        {
            int x = (offset % SIZE) + dx;
            int y = (offset / SIZE) + dy;
            if (y > 0 && x > 0 && x < SIZE && y < SIZE)
            {
                int o = y * SIZE + x;
                if (stones[o] == 1)
                {
                    stones[o] = 0;
                    onePositions.Add(o);
                }
            }
        }

        static void MarkPattern(int[] pattern, int dx, int dy, int value)
        {
            int newO;
            newO = (dy + PATTERN_SIZE / 2) * PATTERN_SIZE + (dx + PATTERN_SIZE / 2);
            pattern[newO] = value;
            newO = (-dy + PATTERN_SIZE / 2) * PATTERN_SIZE + (dx + PATTERN_SIZE / 2);
            pattern[newO] = value;
            newO = (dy + PATTERN_SIZE / 2) * PATTERN_SIZE + (-dx + PATTERN_SIZE / 2);
            pattern[newO] = value;
            newO = (-dy + PATTERN_SIZE / 2) * PATTERN_SIZE + (-dx + PATTERN_SIZE / 2);
            pattern[newO] = value;

            newO = (dx + PATTERN_SIZE / 2) * PATTERN_SIZE + (dy + PATTERN_SIZE / 2);
            pattern[newO] = value;
            newO = (-dx + PATTERN_SIZE / 2) * PATTERN_SIZE + (dy + PATTERN_SIZE / 2);
            pattern[newO] = value;
            newO = (dx + PATTERN_SIZE / 2) * PATTERN_SIZE + (-dy + PATTERN_SIZE / 2);
            pattern[newO] = value;
            newO = (-dx + PATTERN_SIZE / 2) * PATTERN_SIZE + (-dy + PATTERN_SIZE / 2);
            pattern[newO] = value;
        }

        static void Parse(List<string> grid, StreamWriter sw)
        {
            var stones = new int[SIZE * SIZE];
            int two = -1;

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
                        if (c == 2)
                            two = offset;
                        if (c == 1)
                            stones[offset] = c;
                    }
                }
            }

            if (two < 0)
                throw new Exception("Missing 2");

            var onePositions = new List<int>();
            for (int dy = -1; dy <= 1; ++dy)
            {
                for (int dx = -1; dx <= 1; ++dx)
                {
                    if (dx == 0 && dy == 0)
                        continue;
                    ClearStone(stones, two, dx, dy, onePositions);
                }
            }

            for (int o = 0; o < SIZE * SIZE; ++o)
            {
                if (stones[o] == 1)
                {
                    onePositions.Add(o);
                }
            }

            var pattern = new int[PATTERN_SIZE * PATTERN_SIZE];
            for (int o = 0; o < PATTERN_SIZE * PATTERN_SIZE; ++o)
                pattern[o] = 1;
            for (int i1 = 0; i1 < onePositions.Count; ++i1)
            {
                int offset1 = onePositions[i1];
                int x1 = offset1 % SIZE;
                int y1 = offset1 / SIZE;

                for (int i2 = 2; i2 < onePositions.Count; ++i2)
                {
                    if (i2 == i1)
                        continue;
                    int offset2 = onePositions[i2];
                    int x2 = offset2 % SIZE;
                    int y2 = offset2 / SIZE;

                    int dx = x1 - x2;
                    int dy = y1 - y2;
                    if (dx >= -PATTERN_SIZE / 2 && dx <= PATTERN_SIZE / 2 && dy >= -PATTERN_SIZE / 2 && dy <= PATTERN_SIZE / 2)
                    {
                        MarkPattern(pattern, dx, dy, 0);

                        if (dx > 0)
                        {
                            for (int dx1 = dx; dx1 <= PATTERN_SIZE / 2; ++dx1)
                                MarkPattern(pattern, dx1, dy, 0);
                        }
                        if (dx < 0)
                        {
                            for (int dx1 = dx; dx1 >= -PATTERN_SIZE / 2; --dx1)
                                MarkPattern(pattern, dx1, dy, 0);
                        }

                        if (dy > 0)
                        {
                            for (int dy1 = dy; dy1 <= PATTERN_SIZE / 2; ++dy1)
                                MarkPattern(pattern, dx, dy1, 0);
                        }
                        if (dy < 0)
                        {
                            for (int dy1 = dy; dy1 >= -PATTERN_SIZE / 2; --dy1)
                                MarkPattern(pattern, dx, dy1, 0);
                        }
                    }
                }
            }

            int count = 0;
            for (int o = 0; o < PATTERN_SIZE * PATTERN_SIZE; ++o)
            {
                if (pattern[o] == 1)
                    ++count;
            }
            var sb2 = new StringBuilder();
            var sb = new StringBuilder();
            sb2.AppendLine("// " + count);
            for (int y = 0; y < PATTERN_SIZE; ++y)
            {
                sb2.Append("//");
                for (int x = 0; x < PATTERN_SIZE; ++x)
                {
                    if (pattern[y * PATTERN_SIZE + x] == 1)
                    {
                        sb.Append("*");
                        sb2.Append("*");
                    }
                    else
                    {
                        sb.Append(" ");
                        sb2.Append(" ");
                    }
                }
                sb.AppendLine();
                sb2.AppendLine();
            }

            var sb3 = new StringBuilder();
            bool first = true;
            int count2 = 0;

            var cleanPattern = (int[])pattern.Clone();

            for (int y = 0; y < PATTERN_SIZE; ++y)
            {
                bool empty = true;
                for (int x = 0; x < PATTERN_SIZE; ++x)
                {
                    if (cleanPattern[y * PATTERN_SIZE + x] == 1)
                    {
                        // no center
                        int dx = x - PATTERN_SIZE / 2;
                        int dy = y - PATTERN_SIZE / 2;
                        if (dx == 0 && y == 0)
                            continue;
                        if (!first)
                        {
                            sb3.Append(",");
                        }
                        first = false;
                        sb3.Append(String.Format("{0},{1}", dx, dy));

                        //if (dx != 0)
                        //    sb3.Append(String.Format("/*{0},{1}*/", -dx, dy));
                        //if (dy != 0)
                        //    sb3.Append(String.Format("/*{0},{1}*/", dx, -dy));
                        //if (dx != 0 && dy != 0)
                        //    sb3.Append(String.Format("/*{0},{1}*/", -dx, -dy));

                        empty = false;
                        ++count2;
                        cleanPattern[y * PATTERN_SIZE + x] = 0;
                        cleanPattern[y * PATTERN_SIZE + (PATTERN_SIZE - 1 - x)] = 0;
                        cleanPattern[(PATTERN_SIZE - 1 - y) * PATTERN_SIZE + x] = 0;
                        cleanPattern[(PATTERN_SIZE - 1 - y) * PATTERN_SIZE + (PATTERN_SIZE - 1 - x)] = 0;

                        //cleanPattern[x * PATTERN_SIZE + y] = 0;
                        //cleanPattern[x * PATTERN_SIZE + (PATTERN_SIZE - 1 - y)] = 0;
                        //cleanPattern[(PATTERN_SIZE - 1 - x) * PATTERN_SIZE + y] = 0;
                        //cleanPattern[(PATTERN_SIZE - 1 - x) * PATTERN_SIZE + (PATTERN_SIZE - 1 - y)] = 0;

                    }
                }
                if (!empty)
                    sb3.AppendLine();
            }
            sb3.AppendLine("};");

            sb2.AppendLine("int pattern[" + (count2 * 2) + "] = {");
            sb2.Append(sb3);

            var content = sb.ToString();
            if (!localHash.Contains(content))
            {
                sw.WriteLine(count);
                sw.Write(content);
                localHash.Add(content);
            }

            if (!globalHash.ContainsKey(count))
                globalHash.Add(count, new HashSet<string>());

            content = sb2.ToString();
            if (!globalHash[count].Contains(content))
            {
                globalHash[count].Add(content);
            }
        }


        static void ParsePattern(List<string> grid, StreamWriter sw)
        {
            var stones = new int[SIZE * SIZE];
            int two = -1;

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
                        if (c == 2)
                            two = offset;
                        if (c == 1)
                            stones[offset] = c;
                    }
                }
            }

            if (two < 0)
                throw new Exception("Missing 2");

            var onePositions = new List<int>();
            for (int dy = -1; dy <= 1; ++dy)
            {
                for (int dx = -1; dx <= 1; ++dx)
                {
                    if (dx == 0 && dy == 0)
                        continue;
                    ClearStone(stones, two, dx, dy, onePositions);
                }
            }

            for (int o = 0; o < SIZE * SIZE; ++o)
            {
                if (stones[o] == 1)
                {
                    onePositions.Add(o);
                }
            }

            var pattern = new int[PATTERN_SIZE * PATTERN_SIZE];
            for (int o = 0; o < PATTERN_SIZE * PATTERN_SIZE; ++o)
                pattern[o] = 0;
            for (int i1 = 0; i1 < onePositions.Count; ++i1)
            {
                int offset1 = onePositions[i1];
                int x1 = offset1 % SIZE;
                int y1 = offset1 / SIZE;

                for (int i2 = 2; i2 < onePositions.Count; ++i2)
                {
                    if (i2 == i1)
                        continue;
                    int offset2 = onePositions[i2];
                    int x2 = offset2 % SIZE;
                    int y2 = offset2 / SIZE;

                    int dx = x1 - x2;
                    int dy = y1 - y2;
                    if (dx >= -PATTERN_SIZE / 2 && dx <= PATTERN_SIZE / 2 && dy >= -PATTERN_SIZE / 2 && dy <= PATTERN_SIZE / 2)
                    {
                        MarkPattern(pattern, dx, dy, 1);
                    }
                }
            }

            int count = 0;
            for (int o = 0; o < PATTERN_SIZE * PATTERN_SIZE; ++o)
            {
                if (pattern[o] == 1)
                    ++count;
            }

            var sb = new StringBuilder();
            for (int y = 0; y < PATTERN_SIZE; ++y)
            {
                for (int x = 0; x < PATTERN_SIZE; ++x)
                {
                    if (pattern[y * PATTERN_SIZE + x] == 1)
                    {
                        sb.Append("*");
                    }
                    else
                    {
                        sb.Append(" ");
                    }
                }
                sb.AppendLine();
            }

            var content = sb.ToString();
            if (!localHashPattern.Contains(content))
            {
                sw.WriteLine(count);
                sw.Write(content);
                localHashPattern.Add(content);
            }
        }
    }
}
