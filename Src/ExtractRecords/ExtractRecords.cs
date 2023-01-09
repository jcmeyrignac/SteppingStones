using HtmlAgilityPack;
using System;
using System.Collections.Generic;
using System.Data.SqlTypes;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ExtractRecords
{
    internal class ExtractRecords
    {
        const string source = "c:/Download/AZsPCs - Stepping Stones - Final Report5.htm";

        static string GetFileName(int order, int record)
        {
            return string.Format("soutput{0:D2}-{1:D3}.txt", order, record);
        }

        static void Main(string[] args)
        {
            foreach (var f in Directory.GetFiles(Directory.GetCurrentDirectory(), "soutput*.txt"))
                File.Delete(f);

            var doc = new HtmlDocument();
            doc.Load(source);
            foreach (var node in doc.DocumentNode.SelectNodes("//td"))
            {
                string text = node.InnerText;
                if (text.Contains("(-"))
                {
                    text = text.Replace(" ", "").Replace("\r", "").Replace("\n", "").Replace("&nbsp;", "").Replace("visualize", "");

                    var grid = new List<string>();
                    var line = new StringBuilder();
                    int max = 0;
                    int count = 0;
                    int number = 0;
                    bool minus = false;
                    foreach (var c in text)
                    {
                        if (c == '(')
                            continue;
                        if (c == '-')
                        {
                            minus = true;
                            continue;
                        }
                        if (char.IsDigit(c))
                        {
                            number = number * 10 + c - '0';
                            continue;
                        }

                        if (number != 0)
                        {
                            if (minus)
                            {
                                for (int i = 0; i < number; i++)
                                {
                                    if (line.Length != 0)
                                        line.Append(" ");
                                    line.Append("000");
                                }
                                minus = false;
                            }
                            else
                            {
                                max = Math.Max(max, number);
                                if (number == 1)
                                    ++count;
                                if (line.Length != 0)
                                    line.Append(" ");
                                line.Append(number.ToString("D3"));
                            }
                            number = 0;
                        }
                        if (c == ',')
                        {
                            continue;
                        }
                        if (c == ')')
                        {
                            grid.Add(line.ToString());
                            line.Clear();
                            continue;
                        }
                        Debugger.Break();
                    }

                    // now we fix the grid table
                    int maxLen = 0;
                    for (int i = 0; i < grid.Count; ++i)
                    {
                        int len = grid[i].Length;
                        if (((len + 1) % 4) != 0)
                            Debugger.Break();
                        maxLen = Math.Max(maxLen, len);
                    }

                    Console.WriteLine(text);

                    using(var sw = new StreamWriter(GetFileName(count, max), true))
                    {
                        for (int i = 0; i < grid.Count; ++i)
                        {
                            while (grid[i].Length < maxLen)
                                grid[i] += " 000";

                            if (grid[i].Length != maxLen)
                                Debugger.Break();
                            sw.WriteLine(grid[i]);
                        }
                        sw.WriteLine("{0},{1}", count, max);
                    }

                }
            }
        }
    }
}
