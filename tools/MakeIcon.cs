// Generates icon.ico (multi-size, 32-bit) for FlatClip. Usage: MakeIcon.exe <output.ico>
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

static class MakeIcon
{
    static GraphicsPath Rounded(RectangleF r, float rad)
    {
        var p = new GraphicsPath();
        float d = rad * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    static Bitmap Draw(int s)
    {
        var b = new Bitmap(s, s, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(b))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.Clear(Color.Transparent);
            float u = s / 32f;

            using (var bg = Rounded(new RectangleF(1 * u, 1 * u, 30 * u, 30 * u), 7.5f * u))
            using (var br = new LinearGradientBrush(new PointF(0, 0), new PointF(s, s), Color.FromArgb(0x6E, 0x9C, 0xFF), Color.FromArgb(0x35, 0x66, 0xF0)))
                g.FillPath(br, bg);

            using (var pen = new Pen(Color.White, Math.Max(1.2f, 2.2f * u)) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                using (var board = Rounded(new RectangleF(9 * u, 8.5f * u, 14 * u, 16.5f * u), 2.5f * u))
                    g.DrawPath(pen, board);
                if (s >= 24)
                {
                    g.DrawLine(pen, 12.5f * u, 15.5f * u, 19.5f * u, 15.5f * u);
                    g.DrawLine(pen, 12.5f * u, 20f * u, 17f * u, 20f * u);
                }
            }
            using (var clip = Rounded(new RectangleF(12.5f * u, 6f * u, 7 * u, 4.5f * u), 1.4f * u))
                g.FillPath(Brushes.White, clip);
        }
        return b;
    }

    static byte[] Dib(Bitmap b)
    {
        int s = b.Width;
        var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        w.Write(40); w.Write(s); w.Write(s * 2); w.Write((short)1); w.Write((short)32);
        w.Write(0); w.Write(0); w.Write(0); w.Write(0); w.Write(0); w.Write(0);
        for (int y = s - 1; y >= 0; y--)
            for (int x = 0; x < s; x++)
            {
                var c = b.GetPixel(x, y);
                w.Write(c.B); w.Write(c.G); w.Write(c.R); w.Write(c.A);
            }
        w.Write(new byte[((s + 31) / 32) * 4 * s]);   // AND mask (unused with 32-bit alpha)
        return ms.ToArray();
    }

    static void Main(string[] args)
    {
        var sizes = new[] { 16, 20, 24, 32, 40, 48, 64, 128, 256 };
        var images = new List<byte[]>();
        foreach (var s in sizes) using (var bmp = Draw(s)) images.Add(Dib(bmp));

        using (var fs = File.Create(args[0]))
        using (var w = new BinaryWriter(fs))
        {
            w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length);
            int offset = 6 + 16 * sizes.Length;
            for (int i = 0; i < sizes.Length; i++)
            {
                w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                w.Write((byte)0); w.Write((byte)0);
                w.Write((short)1); w.Write((short)32);
                w.Write(images[i].Length); w.Write(offset);
                offset += images[i].Length;
            }
            foreach (var img in images) w.Write(img);
        }
    }
}
