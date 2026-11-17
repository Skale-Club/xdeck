// Writes Xdeck's app icon: the volume-knob mark (skaleclub-apps/design/skale-css/marks/xdeck.svg) on a
// signal-cyan rounded square: a dark knob with a cut-out pointer, five lit ticks and two dim ticks.
// At 16..24 px the dim ticks go and the glyph grows so it stays legible. build.ps1 compiles and
// runs this on every build:  MakeIcon.exe <out.ico>
//   MakeIcon --png <size> <out.png> [--bleed]   one PNG of the tile (--bleed: square, no rounding,
//                                               smaller glyph, for icons the system masks)
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
        // MakeIcon --mask <in.png> <out.png>: a controller silhouette (black body, white details, on
        // transparent) to a mask, white where the body is and see-through elsewhere (Render-PadIcons.ps1)
        if (args.Length == 3 && args[0] == "--mask")
        {
            using (Bitmap src = new Bitmap(args[1]))
            using (Bitmap dst = new Bitmap(src.Width, src.Height, PixelFormat.Format32bppArgb))
            {
                int minX = src.Width, minY = src.Height, maxX = -1, maxY = -1;
                for (int y = 0; y < src.Height; y++)
                    for (int x = 0; x < src.Width; x++)
                    {
                        Color c = src.GetPixel(x, y);
                        float lum = (0.299f * c.R + 0.587f * c.G + 0.114f * c.B) / 255f;
                        int a = (int)Math.Round(c.A * (1 - lum));
                        dst.SetPixel(x, y, Color.FromArgb(a, 255, 255, 255));
                        if (a > 8) { minX = Math.Min(minX, x); minY = Math.Min(minY, y); maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y); }
                    }
                if (maxX < 0) { Console.Error.WriteLine("empty image"); return 1; }
                // trimmed to the drawing, centred in a square
                int side = Math.Max(maxX - minX, maxY - minY) + 1;
                using (Bitmap sq = new Bitmap(side, side, PixelFormat.Format32bppArgb))
                {
                    using (Graphics g = Graphics.FromImage(sq))
                        g.DrawImage(dst, new Rectangle((side - (maxX - minX + 1)) / 2, (side - (maxY - minY + 1)) / 2, maxX - minX + 1, maxY - minY + 1),
                            new Rectangle(minX, minY, maxX - minX + 1, maxY - minY + 1), GraphicsUnit.Pixel);
                    sq.Save(args[2], ImageFormat.Png);
                }
            }
            return 0;
        }
        // MakeIcon --logo <in.png> <out.png>: a brand logo (any colour, on transparent) to a mask, white
        // where the logo is, trimmed tight to it so the banner knows its width (Render-BrandLogos.ps1)
        if (args.Length == 3 && args[0] == "--logo")
        {
            using (Bitmap src = new Bitmap(args[1]))
            {
                int minX = src.Width, minY = src.Height, maxX = -1, maxY = -1;
                for (int y = 0; y < src.Height; y++)
                    for (int x = 0; x < src.Width; x++)
                        if (src.GetPixel(x, y).A > 8) { minX = Math.Min(minX, x); minY = Math.Min(minY, y); maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y); }
                if (maxX < 0) { Console.Error.WriteLine("empty image"); return 1; }
                using (Bitmap dst = new Bitmap(maxX - minX + 1, maxY - minY + 1, PixelFormat.Format32bppArgb))
                {
                    for (int y = minY; y <= maxY; y++)
                        for (int x = minX; x <= maxX; x++)
                            dst.SetPixel(x - minX, y - minY, Color.FromArgb(src.GetPixel(x, y).A, 255, 255, 255));
                    dst.Save(args[2], ImageFormat.Png);
                }
            }
            return 0;
        }
        // MakeIcon --png <size> <out.png> [--bleed]: one PNG of the tile, or of the full-bleed square
        if ((args.Length == 3 || args.Length == 4) && args[0] == "--png")
        {
            int size;
            if (!int.TryParse(args[1], out size) || size < 16 || size > 4096) { Console.Error.WriteLine("bad size " + args[1]); return 1; }
            Save(Draw(size, args.Length == 4 && args[3] == "--bleed", false), args[2]);
            return 0;
        }
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

    // fullBleed: the cyan fills the square (for icons the system masks), with the knob at 56% so it
    // sits inside the centre that every mask keeps. inset: the .ico leaves a 4% margin round the tile.
    static Bitmap Draw(int s, bool fullBleed = false, bool inset = true)
    {
        Bitmap bmp = new Bitmap(s, s, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            Color tile = Color.FromArgb(0x3C, 0xC6, 0xF2), ink = Color.FromArgb(0x04, 0x07, 0x0D);
            float pad = !fullBleed && inset && s > 20 ? s * 0.04f : 0, side = s - 2 * pad, r = side * 0.22f;
            RectangleF box = new RectangleF(pad, pad, side, side);
            using (Brush fill = new SolidBrush(tile))
            {
                if (fullBleed) g.FillRectangle(fill, box);
                else using (GraphicsPath p = Rounded(box, r)) g.FillPath(fill, p);
            }
            bool small = s <= 24;
            float frac = fullBleed ? 0.56f : small ? 0.74f : 0.64f;
            float k = side * frac / 24f;                       // glyph units -> pixels
            Knob(g, box.X + side * (1 - frac) / 2, box.Y + side * (1 - frac) / 2, k, ink, tile, !small);
        }
        return bmp;
    }

    // The mark's glyph on its 24-unit grid, origin (ox, oy), k pixels per unit: the knob, a pointer cut
    // out of it in the pointer colour, five lit ticks and (dim) two ticks at 32%.
    static void Knob(Graphics g, float ox, float oy, float k, Color ink, Color pointer, bool dim)
    {
        using (Brush b = new SolidBrush(ink))
        {
            g.FillEllipse(b, ox + (12f - 5.9f) * k, oy + (12.6f - 5.9f) * k, 11.8f * k, 11.8f * k);
            float[,] lit = { { 5.71f, 18.89f }, { 3.1f, 12.6f }, { 5.71f, 6.31f }, { 12f, 3.7f }, { 18.29f, 6.31f } };
            for (int i = 0; i < 5; i++) Dot(g, b, ox, oy, k, lit[i, 0], lit[i, 1], 1.3f);
        }
        if (dim)
            using (Brush b = new SolidBrush(Color.FromArgb(82, ink)))
            {
                Dot(g, b, ox, oy, k, 20.9f, 12.6f, 1f);
                Dot(g, b, ox, oy, k, 18.29f, 18.89f, 1f);
            }
        using (Pen p = new Pen(pointer, 2f * k))
        {
            p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
            g.DrawLine(p, ox + 12.9f * k, oy + 11.7f * k, ox + 15.2f * k, oy + 9.4f * k);
        }
    }

    static void Dot(Graphics g, Brush b, float ox, float oy, float k, float x, float y, float r)
    {
        g.FillEllipse(b, ox + (x - r) * k, oy + (y - r) * k, 2 * r * k, 2 * r * k);
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
