/*
 *
 * calculer random order
 * */

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace HtmlConv
{
    internal static class HtmlConv
    {
        private static int countSolutions;
        //private const int FontSize1 = 30;
        //private const int FontSize2 = 15;
        private static void Main(string[] args)
        {
            // generate
            countSolutions = 0;

            ParseGrids(args[0]);
        }

        private static void ParseGrids(string filename)
        {

            var grid = new List<string>();
            using (var sr = new StreamReader(filename))
            {
                while (!sr.EndOfStream)
                {
                    var line = sr.ReadLine().Trim();
                    if (string.IsNullOrEmpty(line))
                        continue;

                    if (line.StartsWith("Solution") || line.StartsWith("(") || line.Contains(","))
                    {
                        SaveGrid(grid);
                        grid.Clear();
                        continue;
                    }
                    grid.Add(line);
                }
            }
            SaveGrid(grid);
        }

        private static void SaveGrid(List<string> grid)
        {
            if (grid.Count == 0)
                return;

            ++countSolutions;
            using (var sw = new StreamWriter(countSolutions.ToString("D6") + ".html"))
            {
                sw.WriteLine("<html>");
                sw.WriteLine("<body>");
                sw.WriteLine("<head>");
                sw.WriteLine("<style type=\"text/css\">");
                sw.WriteLine("table {border-spacing:0px;}");
                sw.WriteLine("td {text-align:center;vertical-align:middle;width:20px;}");
                sw.WriteLine(".one {background-color:#808080;}");
                sw.WriteLine("</style>");
                sw.WriteLine("</head>");
                sw.WriteLine("<table border=\"1\">");

                var sb = new StringBuilder();
                for (int i = 0; i < grid.Count; ++i)
                {
                    string line = grid[i];
                    sw.WriteLine("<tr>");

                    var cells = line.Split(' ');

                    if (i > 0)
                        sb.Append(",");

                    int spaces = 0;
                    bool comma = false;
                    sb.Append("(");
                    foreach (var cell in cells)
                    {
                        if (!Int32.TryParse(cell, out int c))
                        {
                            throw new Exception("Error");
                        }

                        if (c == 0)
                            sw.Write("<td></td>");
                        else if (c == 1)
                            sw.Write("<td class=\"one\">1</td>");
                        else if (c < 5)
                            sw.Write("<td><b>{0}</b></td>", c);
                        else
                            sw.Write("<td>{0}</td>", c);

                        if (c == 0)
                        {
                            ++spaces;
                        }
                        else
                        {
                            if (comma)
                                sb.Append(",");
                            if (spaces != 0)
                            {
                                sb.Append(-spaces);
                                sb.Append(",");
                                spaces = 0;
                            }
                            sb.Append(c);
                            comma = true;
                        }
                    }
                    sb.Append(")");
                    sw.WriteLine("</tr>");
                }
                sw.WriteLine("</table>");

                sw.WriteLine("<div>{0}</div>", sb.ToString());

                sw.WriteLine("</body>");
                sw.WriteLine("</html>");
            }

        }


        //private static void GenerateCss(StreamWriter sw)
        //{
        //    sw.WriteLine("<title></title>");
        //    sw.WriteLine("<head>");
        //    sw.WriteLine("<style type=\"text/css\">");
        //    sw.WriteLine("sup{font-size:10px}");
        //    sw.WriteLine(".table1{font:bold 1.3em Arial,Helvetica,sans-serif;text-align:center;border-spacing:0;border-collapse:collapse;}");
        //    sw.WriteLine(".table2{font:bold 1.3em Arial,Helvetica,sans-serif;text-align:right;border-spacing:0;border-collapse:collapse;vertical-align:top;letter-spacing:2px;}");
        //    sw.WriteLine(".up1{width:" + FontSize1 + "px;height:" + FontSize1 + "px;border-top:solid 1px black;border-bottom:none;border-left:none;border-right:none;padding:0;}");
        //    sw.WriteLine(".left1{width:" + FontSize1 + "px;height:" + FontSize1 + "px;border-left:solid 1px black;border-bottom:none;border-top:none;border-right:none;padding:0;}");
        //    sw.WriteLine(".none1{width:" + FontSize1 + "px;height:" + FontSize1 + "px;border-left:none;border-bottom:none;border-top:none;border-right:none;padding:0;}");
        //    sw.WriteLine(
        //        ".upleft1{width:" + FontSize1 + "px;height:" + FontSize1 + "px;border-top:solid 1px black;border-left:solid 1px black;border-bottom:none;border-right:none;padding:0;}");
        //    sw.WriteLine(".black1{width:" + FontSize1 + "px;height:" + FontSize1 + "px;border-left:solid 1px black;border-bottom:none;border-top:solid 1px black;border-right:none;padding:0;background-color:black}");
        //    sw.WriteLine(".indice1{font-size:0.8em;width:30px;height:30px;border-left:none;border-bottom:none;border-top:none;border-right:none;padding:0;}");

        //    sw.WriteLine(".up2{width:" + FontSize2 + "px;height:" + FontSize2 + "px;border-top:solid 1px black;border-bottom:none;border-left:none;border-right:none;padding:0;}");
        //    sw.WriteLine(".left2{width:" + FontSize2 + "px;height:" + FontSize2 + "px;border-left:solid 1px black;border-bottom:none;border-top:none;border-right:none;padding:0;}");
        //    sw.WriteLine(".none2{width:" + FontSize2 + "px;height:" + FontSize2 + "px;border-left:none;border-bottom:none;border-top:none;border-right:none;padding:0;}");
        //    sw.WriteLine(
        //        ".upleft2{width:" + FontSize2 + "px;height:" + FontSize2 + "px;border-top:solid 1px black;border-left:solid 1px black;border-bottom:none;border-right:none;padding:0;}");
        //    sw.WriteLine(".black2{width:" + FontSize2 + "px;height:" + FontSize2 + "px;border-left:solid 1px black;border-bottom:none;border-top:solid 1px black;border-right:none;padding:0;background-color:black}");
        //    sw.WriteLine(".indice2{font-size:0.9em;width:30px;height:30px;border-left:none;border-bottom:none;border-top:none;border-right:none;padding:0;}");
        //    sw.WriteLine("</style>");
        //    sw.WriteLine("</head>");
        //}


        //private static void GenerateMultiCharHtml(string prefix)
        //{
        //    string filename = prefix + "blacksolution" + gridNumber + ".html";
        //    using (var sw = new StreamWriter(filename))
        //    {
        //        sw.WriteLine("<html>");
        //        GenerateCss(sw);
        //        sw.WriteLine("<body>");
        //        GenerateMultiCharGrid(sw, true, true, false, false, false);
        //        sw.WriteLine("</body>");
        //        sw.WriteLine("</html>");
        //    }
        //}

        //private static void GenerateMultiCharGrid(StreamWriter sw, bool solution, bool useBlack, bool renumber, bool diagonal, bool french)
        //{
        //    sw.WriteLine("<table class=\"table" + (renumber && string.IsNullOrEmpty(mask) ? "2" : "1") + "\">");

        //    for (int y = 0; y <= _height; ++y)
        //    {
        //        sw.WriteLine("<tr>");
        //        if (french)
        //        {
        //            if (y < _height)
        //                sw.Write("<td class=\"indice1\">" + (y + 1) + "</td>");
        //            else
        //                sw.Write("<td class=\"indice1\">&nbsp;</td>");
        //        }
        //        for (int x = 0; x <= _width; x += nbLettersByCell)
        //        {
        //            var current = GetChar(x, y);
        //            if (current == " ")
        //            {
        //                current = "_";
        //            }
        //            if (!useBlack && current == "_")
        //            {
        //                current = null;
        //            }

        //            var above = GetChar(x, y - 1);
        //            if (!useBlack && above == " ")
        //            {
        //                above = null;
        //            }
        //            var left = GetChar(x - nbLettersByCell, y);
        //            if (!useBlack && left == " ")
        //            {
        //                left = null;
        //            }
        //            string classe = "upleft1";
        //            string[] characters = new string[nbLettersByCell];
        //            characters[0] = " ";
        //            for (int l = 1; l < nbLettersByCell; ++l)
        //            {
        //                characters[l] = string.Empty;
        //            }
        //            if (solution || (diagonal && x == y))
        //            {
        //                characters[0] = current;
        //                for (int l = 1; l < nbLettersByCell; ++l)
        //                    characters[l] = GetChar(x + l, y);
        //            }
        //            else if (renumber && current != null)
        //            {
        //                if (mask.IndexOf(current) >= 0)
        //                {
        //                    characters[0] = current;
        //                }
        //                else
        //                {
        //                    characters[0] = "&nbsp;<sup>" + (order.IndexOf(current) + 1).ToString() + "</sup>";
        //                }
        //            }
        //            if (current == null)
        //            {
        //                for (int l = 0; l < nbLettersByCell; ++l)
        //                    characters[l] = "&nbsp;";
        //                if (left == null && above == null)
        //                {
        //                    classe = "none1";
        //                }
        //                else if (left == null)
        //                {
        //                    classe = "up1";
        //                }
        //                else if (above == null)
        //                {
        //                    classe = "left1";
        //                }
        //            }
        //            else if (current == "_")
        //            {
        //                classe = useBlack ? "black1" : "none1";
        //                for (int l = 0; l < nbLettersByCell; ++l)
        //                    characters[l] = " ";
        //            }
        //            else
        //            {
        //                //for (int l = 0; l < nbLettersByCell; ++l)
        //                //    if (characters[l] != " ") ++countCharacters;
        //            }
        //            sw.Write("<td class=\"" + classe + "\">");
        //            for (int l = 0; l < nbLettersByCell; ++l)
        //                sw.Write(characters[l]);
        //            sw.Write("</td>");
        //        }
        //        sw.WriteLine("</tr>");
        //    }
        //    sw.WriteLine("</table>");
        //    int countCharacters = CountCharacters();
        //    sw.WriteLine("Size: " + _width + "*" + _height + "=" + (_height * _width) + "<br/>");
        //    sw.WriteLine("Percentage: " + (countCharacters * 100.0 / (float)(_width * _height)) + "%<br/>");
        //    sw.WriteLine("Nb words: " + _wordList.Count + "<br/>");
        //    sw.WriteLine("Characters: " + countCharacters + "<br/>");
        //    sw.WriteLine("Intersections: " + CountIntersections() + "<br/>");
        //    sw.WriteLine("Word length: " + CountWordLength() + "<br/>");
        //    foreach (var s in _grid)
        //    {
        //        sw.WriteLine(s + "<br/>");
        //    }
    }
}
