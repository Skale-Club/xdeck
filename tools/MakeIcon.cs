// Writes Xdeck's app icon: a white gear (the same 8-tooth gear src\Xdeck.cs draws in the tray) on a
// rounded Windows-blue square, so it reads on light and dark backgrounds. build.ps1 compiles and
// runs this on every build:  MakeIcon.exe <out.ico>
// 16..64 px are 32-bit bitmaps (every Windows tool reads those), 256 px is PNG.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

static class MakeIcon
{
    static int Main(string[] args)
    {
        // MakeIcon --png <dir>: the phone remote app's icons (remote\public)
        if (args.Length == 2 && args[0] == "--png")
        {
            Directory.CreateDirectory(args[1]);
            Save(Draw(192), Path.Combine(args[1], "icon-192.png"));
            Save(Draw(512), Path.Combine(args[1], "icon-512.png"));
            Save(Draw(512, true), Path.Combine(args[1], "icon-maskable-512.png"));   // Android crops it to its own shape
            Save(Draw(180, true), Path.Combine(args[1], "apple-touch-icon.png"));    // iOS rounds the corners itself
            return 0;
        }
        if (args.Length != 1) { Console.Error.WriteLine("usage: MakeIcon <out.ico> | MakeIcon --png <dir>"); return 1; }
        int[] sizes = { 16, 20, 24, 32, 40, 48, 64, 256 };
        List<byte[]> images = new List<byte[]>();
        foreach (int s in sizes)
            using (Bitmap b = Draw(s)) images.Add(s >= 256 ? Png(b) : Dib(b));

        using (BinaryWriter w = new BinaryWriter(File.Create(args[0])))
        {
            w.Write((ushort)0); w.Write((ushort)1); w.Write((ushort)sizes.Length);   // ICONDIR
            int offset = 6 + 16 * sizes.Length;
            for (int i = 0; i < sizes.Length; i++)                                  // ICONDIRENTRY
            {
                w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                w.Write((byte)0); w.Write((byte)0);
                w.Write((ushort)1); w.Write((ushort)32);
                w.Write(images[i].Length); w.Write(offset);
                offset += images[i].Length;
            }
            foreach (byte[] img in images) w.Write(img);
        }
        return 0;
    }

    static void Save(Bitmap b, string path) { using (b) b.Save(path, ImageFormat.Png); }

    // fullBleed: the blue fills the square (for icons the system masks), with the gear inside the
    // centre 80% that every mask keeps
    static Bitmap Draw(int s, bool fullBleed = false)
    {
        Bitmap bmp = new Bitmap(s, s, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            float pad = fullBleed || s <= 20 ? 0 : s * 0.04f, side = s - 2 * pad, r = side * 0.22f;
            RectangleF box = new RectangleF(pad, pad, side, side);
            using (LinearGradientBrush fill = new LinearGradientBrush(box, Color.FromArgb(0x1A, 0x8C, 0xE6), Color.FromArgb(0x00, 0x62, 0xB8), 90f))
            {
                if (fullBleed) g.FillRectangle(fill, box);
                else using (GraphicsPath tile = Rounded(box, r)) g.FillPath(fill, tile);
            }
            Gear(g, s / 2f, s / 2f, side * (fullBleed ? 0.28f : s <= 24 ? 0.40f : 0.34f), Color.White);
        }
        return bmp;
    }

    static GraphicsPath Rounded(RectangleF b, float r)
    {
        GraphicsPath p = new GraphicsPath();
        float d = 2 * r;
        p.AddArc(b.X, b.Y, d, d, 180, 90);
        p.AddArc(b.Right - d, b.Y, d, d, 270, 90);
        p.AddArc(b.Right - d, b.Bottom - d, d, d, 0, 90);
        p.AddArc(b.X, b.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    // Same geometry as XdeckApp.Gear in src\Xdeck.cs.
    static void Gear(Graphics g, float cx, float cy, float r, Color c)
    {
        const int teeth = 8;
        float rOut = r, rIn = r * 0.74f;
        List<PointF> pts = new List<PointF>();
        for (int i = 0; i < teeth; i++)
        {
            double a = i * 2 * Math.PI / teeth;
            double half = Math.PI / teeth;
            double[] angles = { a - half * 0.55, a - half * 0.38, a + half * 0.38, a + half * 0.55 };
            float[] radii = { rIn, rOut, rOut, rIn };
            for (int k = 0; k < 4; k++)
                pts.Add(new PointF(cx + (float)(Math.Cos(angles[k]) * radii[k]), cy + (float)(Math.Sin(angles[k]) * radii[k])));
            double gapA = a + half;
            pts.Add(new PointF(cx + (float)(Math.Cos(gapA) * rIn * 0.98), cy + (float)(Math.Sin(gapA) * rIn * 0.98)));
        }
        using (GraphicsPath p = new GraphicsPath(FillMode.Alternate))
        {
            p.AddPolygon(pts.ToArray());
            float h = r * 0.34f;
            p.AddEllipse(cx - h, cy - h, 2 * h, 2 * h);
            using (Brush b = new SolidBrush(c)) g.FillPath(b, p);
        }
    }

    static byte[] Png(Bitmap b)
    {
        using (MemoryStream m = new MemoryStream()) { b.Save(m, ImageFormat.Png); return m.ToArray(); }
    }

    // BITMAPINFOHEADER + bottom-up BGRA pixels + an all-clear AND mask (the alpha does the work).
    static byte[] Dib(Bitmap b)
    {
        int s = b.Width, maskRow = ((s + 31) / 32) * 4;
        BitmapData d = b.LockBits(new Rectangle(0, 0, s, s), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        byte[] px = new byte[s * s * 4];
        for (int y = 0; y < s; y++)                                   // top-down -> bottom-up
            Marshal.Copy(IntPtr.Add(d.Scan0, y * d.Stride), px, (s - 1 - y) * s * 4, s * 4);
        b.UnlockBits(d);
        using (MemoryStream m = new MemoryStream())
        using (BinaryWriter w = new BinaryWriter(m))
        {
            w.Write(40); w.Write(s); w.Write(s * 2);                  // height covers XOR + AND
            w.Write((ushort)1); w.Write((ushort)32);
            w.Write(0); w.Write(px.Length + maskRow * s);
            w.Write(0); w.Write(0); w.Write(0); w.Write(0);
            w.Write(px);
            w.Write(new byte[maskRow * s]);
            return m.ToArray();
        }
    }
}
