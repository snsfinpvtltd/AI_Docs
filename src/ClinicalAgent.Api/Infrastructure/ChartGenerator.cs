using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using ScottPlot;
using ScottPlot.TickGenerators;
using A      = DocumentFormat.OpenXml.Drawing;
using DWP    = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using Pic    = DocumentFormat.OpenXml.Drawing.Pictures;
using WDraw  = DocumentFormat.OpenXml.Wordprocessing.Drawing; // disambiguate from ScottPlot.Drawing

namespace ClinicalAgent.Api.Infrastructure;

/// <summary>A named PNG chart image ready to embed in any output format.</summary>
internal sealed record ChartSpec(string Title, byte[] PngBytes);

/// <summary>
/// Analyses patient data columns and generates PNG chart images using ScottPlot.
/// Also provides helpers for embedding those images into Word (OpenXML) documents.
/// </summary>
internal static class ChartGenerator
{
    private const int ChartWidth  = 600;
    private const int ChartHeight = 340;

    private static readonly string[] PreferredCategorical =
        ["treatment", "group", "gender", "sex", "status", "outcome", "site", "arm", "response", "visit"];

    private static readonly string[] PreferredNumeric =
        ["age", "weight", "height", "bmi", "dose", "score", "value", "duration", "count"];

    // ── PDF table helpers ─────────────────────────────────────────────────────

    /// <summary>
    /// Formats a raw cell value for display: strips zero-time from date-times,
    /// truncates long strings to prevent excessive cell wrapping.
    /// </summary>
    public static string FormatCell(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        if (value.Contains(':') && DateTime.TryParse(value, out var dt))
            return dt.TimeOfDay == TimeSpan.Zero
                ? dt.ToString("dd-MMM-yyyy")
                : dt.ToString("dd-MMM-yyyy HH:mm");
        return value.Length > 30 ? value[..27] + "…" : value;
    }

    /// <summary>
    /// Estimates relative column widths by sampling actual content length.
    /// Longer content gets proportionally more width (clamped 4–25 units).
    /// </summary>
    public static float[] CalcColWeights(IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<string>> rows)
    {
        var sample = rows.Count > 100 ? rows.Take(100) : rows;
        return headers.Select((h, i) =>
        {
            var maxData = sample.Any() ? sample.Max(r => i < r.Count ? FormatCell(r[i]).Length : 0) : 0;
            return (float)Math.Clamp(Math.Max(h.Length, maxData), 4, 25);
        }).ToArray();
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>Returns up to 3 chart specs (PNG bytes) from the most insightful data columns.</summary>
    public static List<ChartSpec> GenerateCharts(
        IReadOnlyList<string> headers,
        IReadOnlyList<IReadOnlyList<string>> rows)
    {
        var specs = new List<ChartSpec>();
        if (rows.Count == 0 || headers.Count == 0) return specs;

        foreach (var (name, counts) in FindCategoricalColumns(headers, rows).Take(2))
        {
            try { specs.Add(new ChartSpec($"{name} Distribution", CategoryBarChart($"{name} Distribution", counts))); }
            catch { /* skip if rendering fails */ }
        }

        foreach (var (name, values) in FindNumericColumns(headers, rows).Take(1))
        {
            try { specs.Add(new ChartSpec($"{name} Histogram", NumericHistogram(name, values))); }
            catch { /* skip if rendering fails */ }
        }

        return specs;
    }

    /// <summary>Embeds a PNG chart image as an inline image in an OpenXML Word document body.</summary>
    public static void AddToWordDocument(
        MainDocumentPart mainPart,
        Body body,
        byte[] pngBytes,
        string altText,
        uint imageId)
    {
        var imagePart = mainPart.AddImagePart(ImagePartType.Png);
        using var imgMs = new MemoryStream(pngBytes);
        imagePart.FeedData(imgMs);
        var relId = mainPart.GetIdOfPart(imagePart);

        // 14.8 cm × 8.5 cm in English Metric Units (1 cm = 360,000 EMU)
        long widthEmu  = 5328000L;
        long heightEmu = 3060000L;

        var drawing = new WDraw(
            new DWP.Inline(
                new DWP.Extent { Cx = widthEmu, Cy = heightEmu },
                new DWP.EffectExtent { LeftEdge = 0L, TopEdge = 0L, RightEdge = 0L, BottomEdge = 0L },
                new DWP.DocProperties { Id = imageId, Name = altText },
                new DWP.NonVisualGraphicFrameDrawingProperties(
                    new A.GraphicFrameLocks { NoChangeAspect = true }),
                new A.Graphic(
                    new A.GraphicData(
                        new Pic.Picture(
                            new Pic.NonVisualPictureProperties(
                                new Pic.NonVisualDrawingProperties { Id = 0U, Name = altText },
                                new Pic.NonVisualPictureDrawingProperties()),
                            new Pic.BlipFill(
                                new A.Blip { Embed = relId },
                                new A.Stretch(new A.FillRectangle())),
                            new Pic.ShapeProperties(
                                new A.Transform2D(
                                    new A.Offset { X = 0L, Y = 0L },
                                    new A.Extents { Cx = widthEmu, Cy = heightEmu }),
                                new A.PresetGeometry(new A.AdjustValueList())
                                    { Preset = A.ShapeTypeValues.Rectangle })))
                    { Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture" }))
            { DistanceFromTop = 0U, DistanceFromBottom = 0U, DistanceFromLeft = 0U, DistanceFromRight = 0U });

        body.Append(new Paragraph(new Run(drawing)));
    }

    // ── Column analysis ───────────────────────────────────────────────────────

    private static List<(string Name, Dictionary<string, int> Counts)> FindCategoricalColumns(
        IReadOnlyList<string> headers,
        IReadOnlyList<IReadOnlyList<string>> rows)
    {
        var scored = new List<(int Score, int Idx)>();

        for (int i = 0; i < headers.Count; i++)
        {
            var lh = headers[i].ToLowerInvariant().Replace("_", "").Replace(" ", "");

            var nonEmpty = rows.Select(r => i < r.Count ? r[i]?.Trim() ?? "" : "")
                               .Where(v => v.Length > 0).ToList();
            if (nonEmpty.Count < 3) continue;

            var distinct = nonEmpty.Distinct(StringComparer.OrdinalIgnoreCase).Count();
            if (distinct < 2 || distinct > 15) continue;

            // Column must be mostly non-numeric to count as categorical
            var numericFraction = (double)nonEmpty.Count(v => double.TryParse(v, out _)) / nonEmpty.Count;
            if (numericFraction > 0.5) continue;

            var score = PreferredCategorical.Any(p => lh.Contains(p)) ? 10 : 1;
            scored.Add((score, i));
        }

        return scored.OrderByDescending(x => x.Score)
                     .Take(2)
                     .Select(x =>
                     {
                         var counts = rows
                             .Select(r => x.Idx < r.Count ? r[x.Idx]?.Trim() ?? "" : "")
                             .Where(v => v.Length > 0)
                             .GroupBy(v => v, StringComparer.OrdinalIgnoreCase)
                             .ToDictionary(g => g.Key, g => g.Count());
                         return (headers[x.Idx], counts);
                     })
                     .ToList();
    }

    private static List<(string Name, List<double> Values)> FindNumericColumns(
        IReadOnlyList<string> headers,
        IReadOnlyList<IReadOnlyList<string>> rows)
    {
        var scored = new List<(int Score, int Idx)>();

        for (int i = 0; i < headers.Count; i++)
        {
            var lh = headers[i].ToLowerInvariant().Replace("_", "").Replace(" ", "");
            var parsed = rows.Select(r => i < r.Count ? r[i] : "")
                             .Where(v => !string.IsNullOrEmpty(v))
                             .Select(v => (Ok: double.TryParse(v, out var d), Val: d))
                             .Where(x => x.Ok)
                             .Select(x => x.Val)
                             .ToList();
            if (parsed.Count < 5) continue;

            var score = PreferredNumeric.Any(p => lh.Contains(p)) ? 10 : 1;
            scored.Add((score, i));
        }

        return scored.OrderByDescending(x => x.Score)
                     .Take(1)
                     .Select(x =>
                     {
                         var values = rows.Select(r => x.Idx < r.Count ? r[x.Idx] : "")
                                          .Where(v => !string.IsNullOrEmpty(v))
                                          .Select(v => (Ok: double.TryParse(v, out var d), Val: d))
                                          .Where(x => x.Ok)
                                          .Select(x => x.Val)
                                          .ToList();
                         return (headers[x.Idx], values);
                     })
                     .ToList();
    }

    // ── Chart rendering ───────────────────────────────────────────────────────

    private static byte[] CategoryBarChart(string title, Dictionary<string, int> counts)
    {
        var sorted = counts.OrderByDescending(kv => kv.Value).Take(12).ToList();
        var labels = sorted.Select(kv => kv.Key).ToArray();
        var values = sorted.Select(kv => (double)kv.Value).ToArray();

        var plt = new Plot();
        plt.Add.Bars(values);

        var tickGen = new NumericManual();
        for (int i = 0; i < labels.Length; i++)
            tickGen.AddMajor(i, labels[i]);
        plt.Axes.Bottom.TickGenerator = tickGen;

        if (labels.Length > 5)
            plt.Axes.Bottom.TickLabelStyle.Rotation = 30;

        plt.Axes.Left.Label.Text    = "Count";
        plt.Axes.Bottom.Label.Text  = "";
        plt.Title(title);
        plt.FigureBackground.Color  = Colors.White;

        return plt.GetImage(ChartWidth, ChartHeight).GetImageBytes();
    }

    private static byte[] NumericHistogram(string columnName, List<double> values)
    {
        var min = values.Min();
        var max = values.Max();
        if (Math.Abs(max - min) < 0.001) max = min + 1;

        var binCount  = Math.Min(12, (int)Math.Ceiling(1 + 3.322 * Math.Log10(values.Count)));
        var binWidth  = (max - min) / binCount;
        var bins      = new double[binCount];
        var binLabels = new string[binCount];

        for (int b = 0; b < binCount; b++)
        {
            var lo = min + b * binWidth;
            var hi = lo + binWidth;
            bins[b]      = values.Count(v => v >= lo && (b == binCount - 1 ? v <= hi : v < hi));
            binLabels[b] = $"{lo:F0}–{hi:F0}";
        }

        var plt = new Plot();
        plt.Add.Bars(bins);

        var tickGen = new NumericManual();
        for (int i = 0; i < binLabels.Length; i++)
            tickGen.AddMajor(i, binLabels[i]);
        plt.Axes.Bottom.TickGenerator = tickGen;
        plt.Axes.Bottom.TickLabelStyle.Rotation = 30;

        plt.Axes.Left.Label.Text    = "Frequency";
        plt.Axes.Bottom.Label.Text  = columnName;
        plt.Title($"{columnName} Histogram");
        plt.FigureBackground.Color  = Colors.White;

        return plt.GetImage(ChartWidth, ChartHeight).GetImageBytes();
    }
}
