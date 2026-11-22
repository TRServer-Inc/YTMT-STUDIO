using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace YTMT_STUDIO.Controls;

/// <summary>
/// Lightweight WinForms history chart. It has no external charting dependency.
/// Values are percentages from 0 to 100.
/// </summary>
public sealed class StatsHistoryChart : Control
{
    private const int MaximumSamples = 60;
    private readonly Queue<(double? Cpu, double? Memory)> _samples = new();

    private static readonly Color BackgroundColor = Color.FromArgb(17, 22, 29);
    private static readonly Color GridColor = Color.FromArgb(42, 49, 59);
    private static readonly Color CpuColor = Color.FromArgb(88, 166, 255);
    private static readonly Color MemoryColor = Color.FromArgb(63, 185, 80);
    private static readonly Color MutedColor = Color.FromArgb(139, 148, 158);

    public StatsHistoryChart()
    {
        DoubleBuffered = true;
        BackColor = BackgroundColor;
        MinimumSize = new Size(280, 140);
        SetStyle(ControlStyles.ResizeRedraw | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint, true);
    }

    public void AddSample(double? cpuPercent, double? memoryPercent)
    {
        _samples.Enqueue((Normalize(cpuPercent), Normalize(memoryPercent)));
        while (_samples.Count > MaximumSamples)
            _samples.Dequeue();
        Invalidate();
    }

    public void ClearSamples()
    {
        _samples.Clear();
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Graphics graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(BackgroundColor);

        var plot = new Rectangle(54, 35, Math.Max(80, ClientSize.Width - 70), Math.Max(60, ClientSize.Height - 55));
        using var gridPen = new Pen(GridColor, 1);
        using var textBrush = new SolidBrush(MutedColor);
        using var cpuPen = new Pen(CpuColor, 2.2F) { LineJoin = LineJoin.Round };
        using var memoryPen = new Pen(MemoryColor, 2.2F) { LineJoin = LineJoin.Round };

        DrawLegend(graphics, 12, 10, CpuColor, "PROCESS CPU", MemoryColor, "CONTAINER RAM");

        for (int value = 0; value <= 100; value += 25)
        {
            int y = plot.Bottom - (int)Math.Round(plot.Height * value / 100d);
            graphics.DrawLine(gridPen, plot.Left, y, plot.Right, y);
            TextRenderer.DrawText(graphics, value.ToString(), Font, new Point(10, y - 8), MutedColor,
                TextFormatFlags.Right | TextFormatFlags.NoPadding);
        }

        (double? Cpu, double? Memory)[] samples = _samples.ToArray();
        if (samples.Length < 2)
        {
            TextRenderer.DrawText(graphics, "Geçmiş grafiği için en az iki başarılı ölçüm gerekiyor.",
                Font, plot, MutedColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.EndEllipsis);
            return;
        }

        DrawSeries(graphics, samples.Select(s => s.Cpu).ToArray(), plot, cpuPen);
        DrawSeries(graphics, samples.Select(s => s.Memory).ToArray(), plot, memoryPen);
    }

    private static void DrawLegend(Graphics graphics, int x, int y, Color firstColor, string firstText,
        Color secondColor, string secondText)
    {
        using var firstBrush = new SolidBrush(firstColor);
        using var secondBrush = new SolidBrush(secondColor);
        using var textBrush = new SolidBrush(MutedColor);
        graphics.FillEllipse(firstBrush, x, y + 3, 8, 8);
        graphics.DrawString(firstText, SystemFonts.MessageBoxFont, textBrush, x + 13, y);
        graphics.FillEllipse(secondBrush, x + 124, y + 3, 8, 8);
        graphics.DrawString(secondText, SystemFonts.MessageBoxFont, textBrush, x + 137, y);
    }

    private static void DrawSeries(Graphics graphics, double?[] values, Rectangle plot, Pen pen)
    {
        PointF? previous = null;
        for (int i = 0; i < values.Length; i++)
        {
            if (!values[i].HasValue)
            {
                previous = null;
                continue;
            }

            float x = plot.Left + (values.Length <= 1 ? 0 : plot.Width * i / (float)(values.Length - 1));
            float y = plot.Bottom - (float)(plot.Height * values[i]!.Value / 100d);
            var current = new PointF(x, y);
            if (previous.HasValue)
                graphics.DrawLine(pen, previous.Value, current);
            previous = current;
        }
    }

    private static double? Normalize(double? value) =>
        value.HasValue && double.IsFinite(value.Value) && value.Value >= 0 && value.Value <= 100
            ? value
            : null;
}