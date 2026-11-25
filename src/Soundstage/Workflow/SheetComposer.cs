// No Dalamud types in this file: the offline self-check compiles it as a shared file.
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;

namespace Soundstage.Workflow;

public readonly record struct SheetCell(int X, int Y, int Width, int Height, int LabelY);

public readonly record struct SheetLayout(int Width, int Height, int Rows, IReadOnlyList<SheetCell> Cells);

/// <summary>Lays N captures out as a labelled grid. The layout is plain arithmetic, so it is checked without drawing anything.</summary>
public static class SheetComposer
{
    public const int Gap = 12;
    public const int LabelHeight = 30;

    public static SheetLayout Layout(int count, int columns, int cellWidth, int cellHeight)
    {
        if (count < 1) throw new ArgumentOutOfRangeException(nameof(count));
        columns = Math.Clamp(columns, 1, count);
        var rows = (count + columns - 1) / columns;
        var rowHeight = cellHeight + LabelHeight;
        var cells = new List<SheetCell>(count);
        for (var i = 0; i < count; i++)
        {
            var col = i % columns;
            var row = i / columns;
            var x = Gap + col * (cellWidth + Gap);
            var y = Gap + row * (rowHeight + Gap);
            cells.Add(new SheetCell(x, y, cellWidth, cellHeight, y + cellHeight));
        }
        return new SheetLayout(Gap + columns * (cellWidth + Gap), Gap + rows * (rowHeight + Gap), rows, cells);
    }

    /// <summary>Draws the grid. Every image is scaled to the cell width; the cell height comes from the first image's shape.</summary>
    public static Bitmap Compose(IReadOnlyList<(string Path, string Label)> items, int columns, int cellWidth)
    {
        if (items.Count == 0) throw new ArgumentException("Nothing to put on the sheet.");
        int cellHeight;
        using (var first = new Bitmap(items[0].Path))
            cellHeight = Math.Max(1, (int)Math.Round(cellWidth * (double)first.Height / first.Width));
        var layout = Layout(items.Count, columns, cellWidth, cellHeight);

        var sheet = new Bitmap(layout.Width, layout.Height, PixelFormat.Format24bppRgb);
        using var g = Graphics.FromImage(sheet);
        g.Clear(Color.FromArgb(24, 24, 28));
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        using var font = new Font("Segoe UI", 11f, FontStyle.Regular, GraphicsUnit.Point);
        using var brush = new SolidBrush(Color.FromArgb(235, 235, 240));
        for (var i = 0; i < items.Count; i++)
        {
            var c = layout.Cells[i];
            using (var img = new Bitmap(items[i].Path))
                g.DrawImage(img, new Rectangle(c.X, c.Y, c.Width, c.Height));
            g.DrawString(items[i].Label, font, brush, new RectangleF(c.X, c.LabelY + 4, c.Width, LabelHeight - 4),
                new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap });
        }
        return sheet;
    }

    /// <summary>Composes and writes the sheet to a temporary name, then renames it, so the final name is never half a file.</summary>
    public static void ComposeToFile(IReadOnlyList<(string Path, string Label)> items, int columns, int cellWidth, string destination)
    {
        var temp = System.IO.Path.ChangeExtension(destination, null) + ".part.png";
        using (var sheet = Compose(items, columns, cellWidth)) sheet.Save(temp, ImageFormat.Png);
        File.Move(temp, destination, overwrite: true);
    }
}
