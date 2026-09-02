using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Print2Md.Core;

internal sealed class LayoutAnalyzer
{
    private static readonly Regex UnorderedListRegex = new Regex(@"^(?:[\u2022\u2023\u25cf\u25a0\u25e6]|[-+*])\s+", RegexOptions.Compiled);
    private static readonly Regex OrderedListRegex = new Regex(@"^(\d+)[.)]\s+", RegexOptions.Compiled);
    private static readonly Regex WhitespaceRegex = new Regex(@"\s+", RegexOptions.Compiled);
    private static readonly Regex DigitsRegex = new Regex(@"\d+", RegexOptions.Compiled);
    private readonly ConversionOptions options;

    public LayoutAnalyzer(ConversionOptions options)
    {
        this.options = options;
    }

    public string Render(XpsDocumentModel document, IList<ConversionWarning> warnings)
    {
        var pageLines = document.Pages.ToDictionary(page => page.Number, CreateLines);
        var repeatedMarginText = FindRepeatedMarginText(document, pageLines);
        var bodyFontSize = FindBodyFontSize(pageLines.Values.SelectMany(lines => lines));
        var output = new StringBuilder();

        foreach (var page in document.Pages)
        {
            var lines = pageLines[page.Number]
                .Where(line => !ShouldRemoveMarginLine(page, line, repeatedMarginText))
                .ToList();
            var orderedLines = options.DetectColumns ? OrderForColumns(page, lines) : lines.OrderBy(line => line.Baseline).ThenBy(line => line.Left).ToList();
            var blocks = RenderTextBlocks(orderedLines, bodyFontSize);

            foreach (var image in page.Images.Where(item => item.Reference != null))
            {
                var reference = image.Reference!;
                var alt = $"Image from page {page.Number}";
                blocks.Add(new MarkdownBlock(image.Y, $"![{alt}]({MarkdownEscaping.LinkDestination(reference.RelativePath)})"));
            }

            if (lines.Count == 0 && page.Images.Count > 0)
            {
                warnings.Add(new ConversionWarning("ocr-not-performed", "This page contains images but no extractable text; OCR was not performed.", page.Number));
                blocks.Insert(0, new MarkdownBlock(-1, $"<!-- Print2Md: page {page.Number} contains image-only content; OCR was not performed. -->"));
            }
            else if (lines.Count == 0 && page.Images.Count == 0)
            {
                warnings.Add(new ConversionWarning("empty-page", "This page contains no extractable text or images.", page.Number));
            }

            AppendBlocks(output, blocks, orderedLines);
        }

        return output.ToString().Trim() + Environment.NewLine;
    }

    private static List<TextLine> CreateLines(XpsPageModel page)
    {
        var lines = new List<TextLine>();
        foreach (var run in page.TextRuns.OrderBy(item => item.Y).ThenBy(item => item.X))
        {
            var tolerance = Math.Max(2, run.FontSize * 0.35);
            var line = lines
                .Where(candidate => Math.Abs(candidate.Baseline - run.Y) <= tolerance)
                .OrderBy(candidate => Math.Abs(candidate.Baseline - run.Y))
                .FirstOrDefault();
            if (line == null)
            {
                line = new TextLine { Baseline = run.Y };
                lines.Add(line);
            }

            line.Runs.Add(run);
            line.Baseline = line.Runs.Average(item => item.Y);
        }

        foreach (var line in lines)
        {
            FinalizeLine(line);
        }

        return lines;
    }

    private HashSet<string> FindRepeatedMarginText(XpsDocumentModel document, IReadOnlyDictionary<int, List<TextLine>> pageLines)
    {
        var repeated = new HashSet<string>(StringComparer.Ordinal);
        if (!options.RemoveRepeatedMargins || document.Pages.Count < options.MinimumRepeatPages)
        {
            return repeated;
        }

        var occurrences = new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);
        foreach (var page in document.Pages)
        {
            foreach (var line in pageLines[page.Number])
            {
                if (line.Top > page.Height * options.MarginFraction && line.Baseline < page.Height * (1 - options.MarginFraction))
                {
                    continue;
                }

                var normalized = NormalizeMarginText(line.PlainText);
                if (normalized.Length < 2)
                {
                    continue;
                }

                if (!occurrences.TryGetValue(normalized, out var pages))
                {
                    pages = new HashSet<int>();
                    occurrences[normalized] = pages;
                }

                pages.Add(page.Number);
            }
        }

        var threshold = Math.Max(options.MinimumRepeatPages, (int)Math.Ceiling(document.Pages.Count * options.RepeatDocumentFraction));
        foreach (var item in occurrences.Where(item => item.Value.Count >= threshold))
        {
            repeated.Add(item.Key);
        }

        return repeated;
    }

    private bool ShouldRemoveMarginLine(XpsPageModel page, TextLine line, ISet<string> repeated)
    {
        var inMargin = line.Top <= page.Height * options.MarginFraction || line.Baseline >= page.Height * (1 - options.MarginFraction);
        return inMargin && repeated.Contains(NormalizeMarginText(line.PlainText));
    }

    private static string NormalizeMarginText(string value)
    {
        value = WhitespaceRegex.Replace(value.Trim().ToLowerInvariant(), " ");
        return DigitsRegex.Replace(value, "#");
    }

    private static double FindBodyFontSize(IEnumerable<TextLine> lines)
    {
        var weightedSizes = new List<double>();
        foreach (var line in lines)
        {
            var weight = Math.Max(1, Math.Min(100, line.PlainText.Length));
            for (var index = 0; index < weight; index++)
            {
                weightedSizes.Add(line.FontSize);
            }
        }

        if (weightedSizes.Count == 0)
        {
            return 12;
        }

        weightedSizes.Sort();
        return weightedSizes[weightedSizes.Count / 2];
    }

    private static List<TextLine> OrderForColumns(XpsPageModel page, List<TextLine> lines)
    {
        if (lines.Count < 3 || page.Width <= 0)
        {
            return lines.OrderBy(line => line.Baseline).ThenBy(line => line.Left).ToList();
        }

        const int bins = 100;
        var crossingCounts = new int[bins];
        var runs = lines.SelectMany(line => line.Runs).ToList();
        for (var index = 0; index < bins; index++)
        {
            var x = page.Width * index / (bins - 1.0);
            crossingCounts[index] = runs.Count(run => run.X <= x && run.X + run.Width >= x);
        }

        var maximumCrossings = Math.Max(1, lines.Count / 10);
        var bestStart = -1;
        var bestEnd = -1;
        var start = -1;
        for (var index = 20; index <= 80; index++)
        {
            if (crossingCounts[index] <= maximumCrossings)
            {
                if (start < 0)
                {
                    start = index;
                }
            }
            else if (start >= 0)
            {
                if (index - start > bestEnd - bestStart)
                {
                    bestStart = start;
                    bestEnd = index - 1;
                }

                start = -1;
            }
        }

        if (start >= 0 && 81 - start > bestEnd - bestStart)
        {
            bestStart = start;
            bestEnd = 80;
        }

        if (bestStart < 0 || bestEnd - bestStart < 5)
        {
            return lines.OrderBy(line => line.Baseline).ThenBy(line => line.Left).ToList();
        }

        var gutterLeft = page.Width * bestStart / (bins - 1.0);
        var gutterRight = page.Width * bestEnd / (bins - 1.0);
        var splitCandidates = lines
            .Where(line => line.Runs.Any(run => run.X + run.Width <= gutterRight) && line.Runs.Any(run => run.X >= gutterLeft))
            .OrderBy(line => line.Baseline)
            .ToList();
        var looksLikeTable = splitCandidates.Count >= 2 && splitCandidates[0].Bold;
        if (!looksLikeTable)
        {
            var expanded = new List<TextLine>();
            foreach (var line in lines)
            {
                var leftRuns = line.Runs.Where(run => run.X + run.Width <= gutterRight).ToList();
                var rightRuns = line.Runs.Where(run => run.X >= gutterLeft).ToList();
                if (leftRuns.Count > 0 && rightRuns.Count > 0)
                {
                    expanded.Add(CreateLine(leftRuns));
                    expanded.Add(CreateLine(rightRuns));
                }
                else
                {
                    expanded.Add(line);
                }
            }

            lines = expanded;
        }

        var spanning = lines.Where(line => line.Left < gutterLeft && line.Right > gutterRight).OrderBy(line => line.Baseline).ToList();
        var remaining = lines.Except(spanning).ToList();
        var ordered = new List<TextLine>();
        var previousBoundary = double.NegativeInfinity;
        foreach (var span in spanning)
        {
            AppendColumnBand(ordered, remaining, previousBoundary, span.Baseline, gutterLeft, gutterRight);
            ordered.Add(span);
            previousBoundary = span.Baseline;
        }

        AppendColumnBand(ordered, remaining, previousBoundary, double.PositiveInfinity, gutterLeft, gutterRight);
        return ordered;
    }

    private static void AppendColumnBand(
        ICollection<TextLine> destination,
        IEnumerable<TextLine> lines,
        double top,
        double bottom,
        double gutterLeft,
        double gutterRight)
    {
        var band = lines.Where(line => line.Baseline > top && line.Baseline < bottom).ToList();
        foreach (var line in band.Where(line => line.Right <= gutterRight).OrderBy(line => line.Baseline).ThenBy(line => line.Left))
        {
            destination.Add(line);
        }

        foreach (var line in band.Where(line => line.Left >= gutterLeft).OrderBy(line => line.Baseline).ThenBy(line => line.Left))
        {
            destination.Add(line);
        }

        foreach (var line in band.Where(line => line.Right > gutterRight && line.Left < gutterLeft).OrderBy(line => line.Baseline).ThenBy(line => line.Left))
        {
            destination.Add(line);
        }
    }

    private List<MarkdownBlock> RenderTextBlocks(IReadOnlyList<TextLine> lines, double bodyFontSize)
    {
        var blocks = new List<MarkdownBlock>();
        var index = 0;
        while (index < lines.Count)
        {
            var line = lines[index];
            var headingLevel = options.DetectHeadings ? GetHeadingLevel(line, bodyFontSize) : 0;
            if (headingLevel > 0)
            {
                blocks.Add(new MarkdownBlock(line.Top, new string('#', headingLevel) + " " + StripOuterEmphasis(line.MarkdownText)));
                index++;
                continue;
            }

            if (options.DetectLists && TryRenderListItem(line, lines, out var listItem))
            {
                blocks.Add(new MarkdownBlock(line.Top, listItem));
                index++;
                continue;
            }

            if (options.DetectSimpleTables && TryRenderTable(lines, index, out var table, out var tableLength))
            {
                blocks.Add(new MarkdownBlock(line.Top, table));
                index += tableLength;
                continue;
            }

            var paragraph = new StringBuilder(line.MarkdownText.Trim());
            var previous = line;
            index++;
            while (index < lines.Count && IsParagraphContinuation(previous, lines[index], bodyFontSize))
            {
                var nextText = lines[index].MarkdownText.Trim();
                if (paragraph.Length > 0 && paragraph[paragraph.Length - 1] == '-' && StartsWithLowercase(lines[index].PlainText))
                {
                    paragraph.Length--;
                }
                else
                {
                    paragraph.Append(' ');
                }

                paragraph.Append(nextText);
                previous = lines[index];
                index++;
            }

            blocks.Add(new MarkdownBlock(line.Top, paragraph.ToString()));
        }

        return blocks;
    }

    private int GetHeadingLevel(TextLine line, double bodyFontSize)
    {
        if (line.PlainText.Length > 160)
        {
            return 0;
        }

        var ratio = line.FontSize / Math.Max(1, bodyFontSize);
        if (ratio >= 1.60) return 1;
        if (ratio >= 1.35) return 2;
        if (ratio >= 1.18) return 3;
        if (ratio >= 1.08 && line.Bold && line.PlainText.Length <= 100) return 4;
        return 0;
    }

    private static bool TryRenderListItem(TextLine line, IReadOnlyList<TextLine> lines, out string markdown)
    {
        markdown = string.Empty;
        var plain = line.PlainText.TrimStart();
        var unordered = UnorderedListRegex.Match(plain);
        var ordered = OrderedListRegex.Match(plain);
        if (!unordered.Success && !ordered.Success)
        {
            return false;
        }

        var minimumIndent = lines.Count == 0 ? line.Left : lines.Min(item => item.Left);
        var level = Math.Max(0, Math.Min(3, (int)Math.Round((line.Left - minimumIndent) / Math.Max(18, line.FontSize * 2))));
        var markerLength = unordered.Success ? unordered.Length : ordered.Length;
        var content = MarkdownEscaping.Inline(plain.Substring(markerLength).Trim());
        var marker = unordered.Success ? "-" : ordered.Groups[1].Value + ".";
        markdown = new string(' ', level * 2) + marker + " " + content;
        return true;
    }

    private static bool TryRenderTable(IReadOnlyList<TextLine> lines, int start, out string markdown, out int length)
    {
        markdown = string.Empty;
        length = 0;
        var firstCells = SplitIntoCells(lines[start]);
        if (firstCells.Count < 2)
        {
            return false;
        }

        var rows = new List<List<string>> { firstCells };
        var index = start + 1;
        while (index < lines.Count && rows.Count < 50)
        {
            var previous = lines[index - 1];
            var current = lines[index];
            if (current.Baseline < previous.Baseline || current.Baseline - previous.Baseline > Math.Max(previous.FontSize, current.FontSize) * 2.2)
            {
                break;
            }

            var cells = SplitIntoCells(current);
            if (cells.Count != firstCells.Count)
            {
                break;
            }

            rows.Add(cells);
            index++;
        }

        if (rows.Count < 2)
        {
            return false;
        }

        var builder = new StringBuilder();
        builder.Append("| ").Append(string.Join(" | ", rows[0].Select(MarkdownEscaping.TableCell))).AppendLine(" |");
        builder.Append("| ").Append(string.Join(" | ", rows[0].Select(_ => "---"))).AppendLine(" |");
        foreach (var row in rows.Skip(1))
        {
            builder.Append("| ").Append(string.Join(" | ", row.Select(MarkdownEscaping.TableCell))).AppendLine(" |");
        }

        markdown = builder.ToString().TrimEnd();
        length = rows.Count;
        return true;
    }

    private static List<string> SplitIntoCells(TextLine line)
    {
        var cells = new List<string>();
        var current = new StringBuilder();
        TextRunModel? previous = null;
        foreach (var run in line.Runs)
        {
            if (previous != null)
            {
                var gap = run.X - (previous.X + previous.Width);
                if (gap >= Math.Max(18, line.FontSize * 2.2))
                {
                    cells.Add(current.ToString().Trim());
                    current.Clear();
                }
                else if (NeedsSpace(previous.Text, run.Text, gap, line.FontSize))
                {
                    current.Append(' ');
                }
            }

            current.Append(run.Text);
            previous = run;
        }

        if (current.Length > 0)
        {
            cells.Add(current.ToString().Trim());
        }

        return cells;
    }

    private bool IsParagraphContinuation(TextLine previous, TextLine current, double bodyFontSize)
    {
        if (GetHeadingLevel(current, bodyFontSize) > 0 ||
            UnorderedListRegex.IsMatch(current.PlainText.TrimStart()) ||
            OrderedListRegex.IsMatch(current.PlainText.TrimStart()) ||
            SplitIntoCells(current).Count > 1)
        {
            return false;
        }

        var verticalGap = current.Baseline - previous.Baseline;
        if (verticalGap < 0 || verticalGap > Math.Max(previous.FontSize, current.FontSize) * 1.8)
        {
            return false;
        }

        if (Math.Abs(current.Left - previous.Left) > Math.Max(previous.FontSize, current.FontSize) * 2.2)
        {
            return false;
        }

        return Math.Abs(current.FontSize - previous.FontSize) <= Math.Max(1.5, bodyFontSize * 0.15);
    }

    private static bool StartsWithLowercase(string value)
    {
        var first = value.TrimStart().FirstOrDefault();
        return first != default && char.IsLower(first);
    }

    private static string StripOuterEmphasis(string value)
    {
        if ((value.StartsWith("***", StringComparison.Ordinal) && value.EndsWith("***", StringComparison.Ordinal)) ||
            (value.StartsWith("___", StringComparison.Ordinal) && value.EndsWith("___", StringComparison.Ordinal)))
        {
            return value.Substring(3, value.Length - 6);
        }

        if (value.Length >= 4 && value.StartsWith("**", StringComparison.Ordinal) && value.EndsWith("**", StringComparison.Ordinal))
        {
            return value.Substring(2, value.Length - 4);
        }

        if (value.Length >= 2 && value.StartsWith("*", StringComparison.Ordinal) && value.EndsWith("*", StringComparison.Ordinal))
        {
            return value.Substring(1, value.Length - 2);
        }

        return value;
    }

    private static string JoinRuns(IReadOnlyList<TextRunModel> runs, bool markdown)
    {
        var builder = new StringBuilder();
        TextRunModel? previous = null;
        foreach (var run in runs)
        {
            if (previous != null)
            {
                var gap = run.X - (previous.X + previous.Width);
                if (NeedsSpace(previous.Text, run.Text, gap, Math.Max(previous.FontSize, run.FontSize)))
                {
                    builder.Append(' ');
                }
            }

            builder.Append(markdown ? RenderRun(run) : run.Text);
            previous = run;
        }

        return WhitespaceRegex.Replace(builder.ToString(), " ").Trim();
    }

    private static TextLine CreateLine(IEnumerable<TextRunModel> runs)
    {
        var line = new TextLine();
        line.Runs.AddRange(runs);
        line.Baseline = line.Runs.Average(run => run.Y);
        FinalizeLine(line);
        return line;
    }

    private static void FinalizeLine(TextLine line)
    {
        line.Runs.Sort((left, right) => left.X.CompareTo(right.X));
        line.Left = line.Runs.Min(run => run.X);
        line.Right = line.Runs.Max(run => run.X + run.Width);
        var totalCharacters = Math.Max(1, line.Runs.Sum(run => run.Text.Length));
        line.FontSize = line.Runs.Sum(run => run.FontSize * Math.Max(1, run.Text.Length)) / totalCharacters;
        line.Bold = line.Runs.Sum(run => run.Bold ? run.Text.Length : 0) >= totalCharacters / 2.0;
        line.Italic = line.Runs.Sum(run => run.Italic ? run.Text.Length : 0) >= totalCharacters / 2.0;
        line.PlainText = JoinRuns(line.Runs, false);
        line.MarkdownText = JoinRuns(line.Runs, true);
    }

    private static bool NeedsSpace(string previous, string current, double gap, double fontSize)
    {
        if (string.IsNullOrEmpty(previous) || string.IsNullOrEmpty(current) || char.IsWhiteSpace(previous[previous.Length - 1]) || char.IsWhiteSpace(current[0]))
        {
            return false;
        }

        if (",.;:!?)]}%".IndexOf(current[0]) >= 0 || "([{\u201c".IndexOf(previous[previous.Length - 1]) >= 0)
        {
            return false;
        }

        return gap > fontSize * 0.12;
    }

    private static string RenderRun(TextRunModel run)
    {
        var value = MarkdownEscaping.Inline(run.Text);
        if (run.Bold && run.Italic) value = "***" + value + "***";
        else if (run.Bold) value = "**" + value + "**";
        else if (run.Italic) value = "*" + value + "*";

        if (!string.IsNullOrWhiteSpace(run.NavigateUri))
        {
            value = "[" + value + "](" + MarkdownEscaping.LinkDestination(run.NavigateUri!) + ")";
        }
        else if (MarkdownEscaping.IsUrl(run.Text))
        {
            value = "<" + run.Text.Trim() + ">";
        }

        return value;
    }

    private static void AppendBlocks(StringBuilder output, List<MarkdownBlock> blocks, IReadOnlyList<TextLine> orderedLines)
    {
        var textOrder = orderedLines.Select((line, index) => new { line.Top, Index = index }).ToList();
        foreach (var block in blocks.OrderBy(block =>
        {
            var nearest = textOrder.Where(item => item.Top >= block.Top).OrderBy(item => item.Top).FirstOrDefault();
            return nearest?.Index ?? int.MaxValue;
        }).ThenBy(block => block.Top))
        {
            if (string.IsNullOrWhiteSpace(block.Markdown))
            {
                continue;
            }

            if (output.Length > 0)
            {
                output.AppendLine().AppendLine();
            }

            output.Append(block.Markdown.Trim());
        }
    }
}
