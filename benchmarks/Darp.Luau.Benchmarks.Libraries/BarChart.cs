using System.Globalization;
using System.Security;
using System.Text;

namespace Darp.Luau.Benchmarks.Libraries;

/// <summary> One bar: a library's result in one scenario. The value is null when the benchmark could not run. </summary>
internal sealed record Bar(string Library, double? Value);

/// <summary> One panel: all libraries in one scenario, on that scenario's own scale. </summary>
internal sealed record Panel(string Title, IReadOnlyList<Bar> Bars);

/// <summary>
/// A grid of small horizontal bar charts, one per scenario, drawn as a self-contained SVG.
/// One library is highlighted and the others are gray; names and values are written next to every bar,
/// so the chart does not depend on telling colors apart.
/// </summary>
internal sealed class BarChart(string title, string subtitle, string footnote, string highlightedLibrary)
{
    private const int Width = 760;
    private const int Padding = 24;
    private const int Columns = 2;
    private const int ColumnGap = 32;
    private const int PanelWidth = (Width - 2 * Padding - (Columns - 1) * ColumnGap) / Columns;
    private const int HeaderHeight = 72;
    private const int PanelTitleHeight = 24;
    private const int RowHeight = 24;
    private const int PanelGap = 24;
    private const int BarHeight = 14;
    private const int BarCornerRadius = 4;
    private const int LabelColumnWidth = 134;
    private const int ValueColumnWidth = 104;
    private const int MaxBarWidth = PanelWidth - LabelColumnWidth - ValueColumnWidth;
    private const int FootnoteHeight = 28;

    // The light and dark values come from one validated palette. The chart draws its own surface, so that it stays
    // readable when the color scheme of the viewer differs from the page it is embedded in.
    private const string Style = """
        text { font-family: system-ui, -apple-system, "Segoe UI", sans-serif; }
        .surface { fill: #fcfcfb; stroke: rgba(11, 11, 11, 0.10); }
        .primary { fill: #0b0b0b; }
        .secondary { fill: #52514e; }
        .muted { fill: #898781; }
        .axis { stroke: #c3c2b7; }
        .highlight { fill: #2a78d6; }
        .context { fill: #898781; }
        .title { font-size: 16px; font-weight: 600; }
        .subtitle, .label, .value { font-size: 12px; }
        .panel-title { font-size: 13px; font-weight: 600; }
        .emphasis { font-weight: 600; }
        .footnote { font-size: 11px; }
        @media (prefers-color-scheme: dark) {
          .surface { fill: #1a1a19; stroke: rgba(255, 255, 255, 0.10); }
          .primary { fill: #ffffff; }
          .secondary { fill: #c3c2b7; }
          .axis { stroke: #383835; }
          .highlight { fill: #3987e5; }
        }
        """;

    private readonly string _title = title;
    private readonly string _subtitle = subtitle;
    private readonly string _footnote = footnote;
    private readonly string _highlightedLibrary = highlightedLibrary;

    public string Render(IReadOnlyList<Panel> panels, Func<double, string> formatValue)
    {
        int rows = (panels.Count + Columns - 1) / Columns;
        int barsPerPanel = panels.Max(panel => panel.Bars.Count);
        int panelHeight = PanelTitleHeight + barsPerPanel * RowHeight;
        int height = HeaderHeight + rows * panelHeight + (rows - 1) * PanelGap + FootnoteHeight + Padding;

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
        svg.AppendLine(Invariant($"""<text class="title primary" x="{Padding}" y="38">{Escape(_title)}</text>"""));
        svg.AppendLine(
            Invariant($"""<text class="subtitle secondary" x="{Padding}" y="58">{Escape(_subtitle)}</text>""")
        );

        for (int index = 0; index < panels.Count; index++)
        {
            int x = Padding + index % Columns * (PanelWidth + ColumnGap);
            int y = HeaderHeight + index / Columns * (panelHeight + PanelGap);
            AppendPanel(svg, panels[index], x, y, formatValue);
        }

        svg.AppendLine(
            Invariant(
                $"""<text class="footnote muted" x="{Padding}" y="{height - Padding + 4}">{Escape(_footnote)}</text>"""
            )
        );
        svg.AppendLine("</svg>");
        return svg.ToString();
    }

    private void AppendPanel(StringBuilder svg, Panel panel, int x, int y, Func<double, string> formatValue)
    {
        svg.AppendLine(
            Invariant($"""<text class="panel-title primary" x="{x}" y="{y + 12}">{Escape(panel.Title)}</text>""")
        );

        double largest = panel.Bars.Max(bar => bar.Value ?? 0);
        double? highlighted = panel.Bars.FirstOrDefault(bar => bar.Library == _highlightedLibrary)?.Value;
        int barLeft = x + LabelColumnWidth;
        int firstRowTop = y + PanelTitleHeight;

        for (int row = 0; row < panel.Bars.Count; row++)
        {
            Bar bar = panel.Bars[row];
            bool isHighlighted = bar.Library == _highlightedLibrary;
            int rowTop = firstRowTop + row * RowHeight;
            int textBaseline = rowTop + RowHeight / 2 + 4;

            string labelClass = isHighlighted ? "label primary emphasis" : "label secondary";
            svg.AppendLine(
                Invariant(
                    $"""<text class="{labelClass}" x="{barLeft - 8}" y="{textBaseline}" text-anchor="end">{Escape(bar.Library)}</text>"""
                )
            );

            if (bar.Value is not { } value)
            {
                svg.AppendLine(
                    Invariant($"""<text class="value muted" x="{barLeft + 6}" y="{textBaseline}">not measured</text>""")
                );
                continue;
            }

            double barWidth = largest > 0 ? Math.Max(value / largest * MaxBarWidth, 1) : 1;
            int barTop = rowTop + (RowHeight - BarHeight) / 2;
            svg.AppendLine(
                Invariant(
                    $"""<path class="{(isHighlighted ? "highlight" : "context")}" d="{BarPath(barLeft, barTop, barWidth)}"/>"""
                )
            );

            string valueClass = isHighlighted ? "value primary emphasis" : "value primary";
            svg.AppendLine(
                Invariant(
                    $"""<text class="{valueClass}" x="{barLeft + barWidth + 6:0.#}" y="{textBaseline}">{Escape(formatValue(value))}<tspan class="muted">{Escape(FormatRatio(value, highlighted, isHighlighted))}</tspan></text>"""
                )
            );
        }

        // The baseline all bars grow from.
        svg.AppendLine(
            Invariant(
                $"""<line class="axis" x1="{barLeft - 0.5}" y1="{firstRowTop}" x2="{barLeft - 0.5}" y2="{firstRowTop + panel.Bars.Count * RowHeight}"/>"""
            )
        );
    }

    /// <summary> A bar that is square at the baseline and rounded at the end that carries the value. </summary>
    private static string BarPath(int left, int top, double width)
    {
        double radius = Math.Min(BarCornerRadius, width / 2);
        return Invariant(
            $"M{left},{top} h{width - radius:0.##} a{radius:0.##},{radius:0.##} 0 0 1 {radius:0.##},{radius:0.##} v{BarHeight - 2 * radius:0.##} a{radius:0.##},{radius:0.##} 0 0 1 {-radius:0.##},{radius:0.##} h{-(width - radius):0.##} z"
        );
    }

    private static string FormatRatio(double value, double? highlighted, bool isHighlighted)
    {
        if (isHighlighted || highlighted is not > 0)
            return "";
        double ratio = value / highlighted.Value;
        return Invariant($"  {ratio.ToString(ratio < 10 ? "0.0" : "0", CultureInfo.InvariantCulture)}×");
    }

    private static string Escape(string text) => SecurityElement.Escape(text);

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
