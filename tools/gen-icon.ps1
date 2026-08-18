# Generates src/KeyPeek/Assets/KeyPeek.ico — a keycap with a "K".
# The drawing + ICO container logic is inline C# (Add-Type) because byte-level file
# formats are painful in pure PowerShell. Run once; the .ico is committed.

$code = @'
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

public static class IconGen
{
    public static Bitmap Draw(int size)
    {
        Bitmap bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
            g.Clear(Color.Transparent);

            int pad = Math.Max(1, (int)(size * 0.04));
            int r = Math.Max(2, (int)(size * 0.22));
            Rectangle rect = new Rectangle(pad, pad, size - 2 * pad, size - 2 * pad);
            int d = 2 * r;

            using (GraphicsPath path = new GraphicsPath())
            {
                path.AddArc(rect.X, rect.Y, d, d, 180, 90);
                path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
                path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
                path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
                path.CloseFigure();

                using (SolidBrush bg = new SolidBrush(Color.FromArgb(255, 27, 27, 38)))
                    g.FillPath(bg, path);
                using (Pen pen = new Pen(Color.FromArgb(255, 124, 108, 255), Math.Max(1f, size * 0.06f)))
                    g.DrawPath(pen, path);
            }

            using (Font font = new Font("Segoe UI", size * 0.52f, FontStyle.Bold, GraphicsUnit.Pixel))
            using (StringFormat fmt = new StringFormat())
            {
                fmt.Alignment = StringAlignment.Center;
                fmt.LineAlignment = StringAlignment.Center;
                g.DrawString("K", font, Brushes.White, new RectangleF(0, size * 0.02f, size, size), fmt);
            }
        }
        return bmp;
    }

    // 32bpp ARGB bitmap -> classic ICO blob: BITMAPINFOHEADER + bottom-up BGRA + empty AND mask
    public static byte[] BmpEntry(Bitmap bmp)
    {
        int s = bmp.Width;
        BitmapData locked = bmp.LockBits(new Rectangle(0, 0, s, s), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        byte[] pixels = new byte[s * s * 4];
        Marshal.Copy(locked.Scan0, pixels, 0, pixels.Length);
        bmp.UnlockBits(locked);

        MemoryStream ms = new MemoryStream();
        BinaryWriter w = new BinaryWriter(ms);
        w.Write((uint)40); w.Write(s); w.Write(s * 2); w.Write((ushort)1); w.Write((ushort)32);
        w.Write((uint)0); w.Write((uint)(s * s * 4));
        w.Write(0); w.Write(0); w.Write((uint)0); w.Write((uint)0);
        int rowBytes = s * 4;
        for (int y = s - 1; y >= 0; y--)
            w.Write(pixels, y * rowBytes, rowBytes);
        int maskStride = ((s + 31) / 32) * 4;
        w.Write(new byte[maskStride * s]);
        w.Flush();
        return ms.ToArray();
    }

    public static byte[] PngEntry(Bitmap bmp)
    {
        MemoryStream ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }

    public static void WriteIco(string path)
    {
        int[] bmpSizes = new int[] { 16, 32, 48 };
        List<int> sizes = new List<int>();
        List<byte[]> blobs = new List<byte[]>();
        foreach (int s in bmpSizes)
        {
            using (Bitmap b = Draw(s)) blobs.Add(BmpEntry(b));
            sizes.Add(s);
        }
        using (Bitmap b256 = Draw(256)) { blobs.Add(PngEntry(b256)); sizes.Add(256); }

        using (FileStream fs = File.Create(path))
        using (BinaryWriter w = new BinaryWriter(fs))
        {
            w.Write((ushort)0); w.Write((ushort)1); w.Write((ushort)blobs.Count);
            int offset = 6 + 16 * blobs.Count;
            for (int i = 0; i < blobs.Count; i++)
            {
                w.Write((byte)(sizes[i] % 256));
                w.Write((byte)(sizes[i] % 256));
                w.Write((byte)0); w.Write((byte)0);
                w.Write((ushort)1); w.Write((ushort)32);
                w.Write((uint)blobs[i].Length);
                w.Write((uint)offset);
                offset += blobs[i].Length;
            }
            for (int i = 0; i < blobs.Count; i++)
                w.Write(blobs[i]);
        }
    }
}
'@

Add-Type -TypeDefinition $code -ReferencedAssemblies System.Drawing
$out = Join-Path (Split-Path $PSScriptRoot -Parent) "src\KeyPeek\Assets\KeyPeek.ico"
New-Item -ItemType Directory -Force (Split-Path $out) | Out-Null
[IconGen]::WriteIco($out)
Write-Output "wrote $out ($((Get-Item $out).Length) bytes)"
