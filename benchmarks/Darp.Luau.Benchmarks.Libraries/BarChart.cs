using System.Globalization;
using System.Security;
using System.Text;

namespace Darp.Luau.Benchmarks.Libraries;

/// <summary> One column of bars: what is measured and how its values are written. </summary>
internal sealed record Measure(string Title, Func<double, string> Format);

/// <summary> One library's results in one scenario, one value per measure. A value is null when the benchmark could not run. </summary>
internal sealed record Row(string Library, IReadOnlyList<double?> Values);

/// <summary> One panel: all libraries in one scenario. Every measure has its own scale within the panel. </summary>
internal sealed record Panel(string Title, IReadOnlyList<Row> Rows);

/// <summary> What the chart says about a library below the panels: the version that was measured and what runs the Lua code. </summary>
internal sealed record LegendEntry(string Library, string Version, string Runtime);

/// <summary>
/// A stack of small horizontal bar charts, one per scenario, with one column of bars per measure,
/// drawn as a self-contained SVG. One library is highlighted and the others are gray; names and values are
/// written next to every bar, so the chart does not depend on telling colors apart.
/// </summary>
internal sealed class BarChart(
    string title,
    string subtitle,
    IReadOnlyList<string> footnotes,
    string highlightedLibrary
)
{
    private const int Width = 840;
    private const int Padding = 28;
    private const int ContentWidth = Width - 2 * Padding;
    private const int HeaderHeight = 84;
    private const int MeasureTitleHeight = 22;
    private const int PanelTitleHeight = 26;
    private const int RowHeight = 20;
    private const int PanelGap = 22;
    private const int BarHeight = 12;
    private const int BarCornerRadius = 4;
    private const int LabelColumnWidth = 92;
    private const int ColumnGap = 28;
    private const int ValueColumnWidth = 112;
    private const int LegendHeight = 58;
    private const int FootnoteLineHeight = 16;

    // The light and dark values come from one validated palette. The chart draws its own surface, so that it stays
    // readable when the color scheme of the viewer differs from the page it is embedded in.
    private const string Style = """
        text { font-family: system-ui, -apple-system, "Segoe UI", sans-serif; }
        .surface { fill: #fcfcfb; stroke: rgba(11, 11, 11, 0.10); }
        .primary { fill: #0b0b0b; }
        .secondary { fill: #52514e; }
        .muted { fill: #898781; }
        .axis { stroke: #c3c2b7; }
        .rule { stroke: #e1e0d9; }
        .highlight { fill: #2a78d6; }
        .context { fill: #898781; }
        .title { font-size: 17px; font-weight: 600; }
        .subtitle { font-size: 13px; }
        .label, .value { font-size: 12px; }
        .measure-title { font-size: 11px; font-weight: 600; letter-spacing: 0.06em; text-transform: uppercase; }
        .panel-title { font-size: 13px; font-weight: 600; }
        .emphasis { font-weight: 600; }
        .footnote { font-size: 11px; }
        @media (prefers-color-scheme: dark) {
          .surface { fill: #1a1a19; stroke: rgba(255, 255, 255, 0.10); }
          .primary { fill: #ffffff; }
          .secondary { fill: #c3c2b7; }
          .axis { stroke: #383835; }
          .rule { stroke: #2c2c2a; }
          .highlight { fill: #3987e5; }
        }
        """;

    private readonly string _title = title;
    private readonly string _subtitle = subtitle;
    private readonly IReadOnlyList<string> _footnotes = footnotes;
    private readonly string _highlightedLibrary = highlightedLibrary;

    public string Render(
        IReadOnlyList<Measure> measures,
        IReadOnlyList<Panel> panels,
        IReadOnlyList<LegendEntry> legend
    )
    {
        int columnWidth = (ContentWidth - LabelColumnWidth - (measures.Count - 1) * ColumnGap) / measures.Count;
        int[] barLefts =
        [
            .. Enumerable
                .Range(0, measures.Count)
                .Select(column => Padding + LabelColumnWidth + column * (columnWidth + ColumnGap)),
        ];
        int maxBarWidth = columnWidth - ValueColumnWidth;

        var body = new StringBuilder();
        body.AppendLine(Invariant($"""<text class="title primary" x="{Padding}" y="44">{Escape(_title)}</text>"""));
        body.AppendLine(
            Invariant($"""<text class="subtitle secondary" x="{Padding}" y="65">{Escape(_subtitle)}</text>""")
        );

        int y = HeaderHeight;
        for (int column = 0; column < measures.Count; column++)
        {
            body.AppendLine(
                Invariant(
                    $"""<text class="measure-title muted" x="{barLefts[column]}" y="{y + 11}">{Escape(measures[column].Title)}</text>"""
                )
            );
        }
        y += MeasureTitleHeight;

        foreach (Panel panel in panels)
        {
            AppendRule(body, y);
            y += PanelGap / 2;
            AppendPanel(body, panel, measures, barLefts, maxBarWidth, y);
            y += PanelTitleHeight + panel.Rows.Count * RowHeight + PanelGap / 2;
        }

        AppendRule(body, y);
        AppendLegend(body, legend, y);
        y += LegendHeight;

        AppendRule(body, y);
        y += 8;
        foreach (string footnote in _footnotes)
        {
            y += FootnoteLineHeight;
            body.AppendLine(
                Invariant($"""<text class="footnote muted" x="{Padding}" y="{y}">{Escape(footnote)}</text>""")
            );
        }
        int height = y + Padding - 6;

        var svg = new StringBuilder();
        svg.AppendLine(
            Invariant(
                $"""<svg xmlns="http://www.w3.org/2000/svg" width="{Width}" height="{height}" viewBox="0 0 {Width} {height}" role="img" aria-label="{Escape(_title)}">"""
            )
        );
        svg.AppendLine($"<style>{Style}</style>");
        svg.AppendLine(
            Invariant($"""<rect class="surface" x="0.5" y="0.5" width="{Width - 1}" height="{height - 1}" rx="8"/>""")
        );
        svg.Append(body);
        svg.AppendLine("</svg>");
        return svg.ToString();
    }

    private void AppendPanel(
        StringBuilder svg,
        Panel panel,
        IReadOnlyList<Measure> measures,
        int[] barLefts,
        int maxBarWidth,
        int y
    )
    {
        svg.AppendLine(
            Invariant($"""<text class="panel-title primary" x="{Padding}" y="{y + 14}">{Escape(panel.Title)}</text>""")
        );

        Row? highlightedRow = panel.Rows.FirstOrDefault(row => row.Library == _highlightedLibrary);
        int firstRowTop = y + PanelTitleHeight;

        for (int rowIndex = 0; rowIndex < panel.Rows.Count; rowIndex++)
        {
            Row row = panel.Rows[rowIndex];
            bool isHighlighted = row.Library == _highlightedLibrary;
            int rowTop = firstRowTop + rowIndex * RowHeight;
            int textBaseline = rowTop + RowHeight / 2 + 4;

            string labelClass = isHighlighted ? "label primary emphasis" : "label secondary";
            svg.AppendLine(
                Invariant(
                    $"""<text class="{labelClass}" x="{barLefts[0] - 10}" y="{textBaseline}" text-anchor="end">{Escape(row.Library)}</text>"""
                )
            );

            for (int column = 0; column < measures.Count; column++)
            {
                int barLeft = barLefts[column];
                if (row.Values[column] is not { } value)
                {
                    svg.AppendLine(
                        Invariant(
                            $"""<text class="value muted" x="{barLeft + 6}" y="{textBaseline}">not measured</text>"""
                        )
                    );
                    continue;
                }

                // A value of zero has no bar; its label sits directly on the baseline.
                double largest = panel.Rows.Max(other => other.Values[column] ?? 0);
                double barWidth = value > 0 ? Math.Max(value / largest * maxBarWidth, 2) : 0;
                if (barWidth > 0)
                {
                    int barTop = rowTop + (RowHeight - BarHeight) / 2;
                    svg.AppendLine(
                        Invariant(
                            $"""<path class="{(isHighlighted ? "highlight" : "context")}" d="{BarPath(barLeft, barTop, barWidth)}"/>"""
                        )
                    );
                }

                string valueClass = isHighlighted ? "value primary emphasis" : "value primary";
                string ratio = isHighlighted ? "" : FormatRatio(value, highlightedRow?.Values[column]);
                svg.AppendLine(
                    Invariant(
                        $"""<text class="{valueClass}" x="{barLeft + barWidth + 6:0.#}" y="{textBaseline}">{Escape(measures[column].Format(value))}<tspan class="muted">{Escape(ratio)}</tspan></text>"""
                    )
                );
            }
        }

        // The baseline all bars of a measure grow from.
        foreach (int barLeft in barLefts)
        {
            svg.AppendLine(
                Invariant(
                    $"""<line class="axis" x1="{barLeft - 0.5}" y1="{firstRowTop}" x2="{barLeft - 0.5}" y2="{firstRowTop + panel.Rows.Count * RowHeight}"/>"""
                )
            );
        }
    }

    private void AppendLegend(StringBuilder svg, IReadOnlyList<LegendEntry> legend, int y)
    {
        int entryWidth = ContentWidth / legend.Count;
        for (int index = 0; index < legend.Count; index++)
        {
            LegendEntry entry = legend[index];
            int x = Padding + index * entryWidth;
            string swatchClass = entry.Library == _highlightedLibrary ? "highlight" : "context";
            svg.AppendLine(
                Invariant($"""<rect class="{swatchClass}" x="{x}" y="{y + 16}" width="10" height="10" rx="2"/>""")
            );
            svg.AppendLine(
                Invariant(
                    $"""<text class="label primary emphasis" x="{x + 16}" y="{y + 25}">{Escape(entry.Library)}<tspan class="secondary" font-weight="400" dx="6">{Escape(entry.Version)}</tspan></text>"""
                )
            );
            svg.AppendLine(
                Invariant(
                    $"""<text class="footnote secondary" x="{x + 16}" y="{y + 42}">{Escape(entry.Runtime)}</text>"""
                )
            );
        }
    }

    private static void AppendRule(StringBuilder svg, int y) =>
        svg.AppendLine(
            Invariant($"""<line class="rule" x1="{Padding}" y1="{y + 0.5}" x2="{Width - Padding}" y2="{y + 0.5}"/>""")
        );

    /// <summary> A bar that is square at the baseline and rounded at the end that carries the value. </summary>
    private static string BarPath(int left, int top, double width)
    {
        double radius = Math.Min(BarCornerRadius, width / 2);
        return Invariant(
            $"M{left},{top} h{width - radius:0.##} a{radius:0.##},{radius:0.##} 0 0 1 {radius:0.##},{radius:0.##} v{BarHeight - 2 * radius:0.##} a{radius:0.##},{radius:0.##} 0 0 1 {-radius:0.##},{radius:0.##} h{-(width - radius):0.##} z"
        );
    }

    private static string FormatRatio(double value, double? highlighted)
    {
        if (highlighted is not > 0)
            return "";
        double ratio = value / highlighted.Value;
        return Invariant($"  {ratio.ToString(ratio < 10 ? "0.0" : "0", CultureInfo.InvariantCulture)}×");
    }

    private static string Escape(string text) => SecurityElement.Escape(text);

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
