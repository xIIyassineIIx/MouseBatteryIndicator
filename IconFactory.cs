using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;

namespace MouseBattery
{
    public static class IconFactory
    {
        static readonly Color Green = Color.FromArgb(46, 204, 113);
        static readonly Color Amber = Color.FromArgb(255, 176, 0);
        static readonly Color Red = Color.FromArgb(255, 69, 58);
        static readonly Color ChargingBlue = Color.FromArgb(56, 189, 248);
        static readonly Color UnknownGray = Color.FromArgb(145, 145, 145);

        static readonly int[] Sizes = { 16, 20, 24, 32, 40, 48, 64 };

        public static Icon Build(Reading r, bool digits, bool lightTheme)
        {
            var frames = new List<Bitmap>();
            try
            {
                foreach (int s in Sizes) frames.Add(Render(s, r, digits, lightTheme));
                byte[] ico = BuildIco(frames);
                using (var ms = new MemoryStream(ico))
                using (var tmp = new Icon(ms))
                    return (Icon)tmp.Clone();
            }
            finally
            {
                foreach (var f in frames) f.Dispose();
            }
        }

        static Bitmap Render(int size, Reading r, bool digits, bool lightTheme)
        {
            var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAlias;
                g.Clear(Color.Transparent);
                bool known = r != null && r.Known;
                int pct = known ? r.Percent : 0;
                bool charging = known && r.Charging;
                Color outline = lightTheme ? Color.FromArgb(45, 45, 45) : Color.FromArgb(242, 242, 242);
                Color level = !known ? UnknownGray : (pct <= 20 ? Red : (pct <= 50 ? Amber : Green));
                if (digits) DrawDigits(g, size, known, pct, charging, level, outline);
                else DrawBattery(g, size, known, pct, charging, level, outline);
            }
            return bmp;
        }

        static void DrawBattery(Graphics g, int size, bool known, int pct, bool charging, Color level, Color outline)
        {
            float s = size / 32f;
            g.ScaleTransform(s, s);
            using (var pen = new Pen(outline, 2f))
            using (var body = RoundRect(new RectangleF(2.5f, 9f, 24f, 14f), 2.5f))
                g.DrawPath(pen, body);
            using (var brush = new SolidBrush(outline))
                g.FillRectangle(brush, 27f, 12.5f, 3f, 7f);

            if (!known)
            {
                using (var brush = new SolidBrush(UnknownGray))
                    g.FillRectangle(brush, 8.5f, 14.5f, 12f, 3f);
                return;
            }

            float w = 18f * Math.Max(0, Math.Min(100, pct)) / 100f;
            if (w > 0.4f)
                using (var brush = new SolidBrush(level))
                    g.FillRectangle(brush, 5.5f, 12f, w, 8f);

            if (charging)
            {
                PointF[] bolt =
                {
                    new PointF(18.5f, 7.5f), new PointF(12.5f, 17.5f),
                    new PointF(16.2f, 17.5f), new PointF(14.2f, 24.5f),
                    new PointF(21.0f, 14.5f), new PointF(17.2f, 14.5f)
                };
                using (var path = new GraphicsPath())
                {
                    path.AddPolygon(bolt);
                    using (var fill = new SolidBrush(Color.White)) g.FillPath(fill, path);
                    using (var pen = new Pen(outline, 1.1f)) g.DrawPath(pen, path);
                }
            }
        }

        static void DrawDigits(Graphics g, int size, bool known, int pct, bool charging, Color level, Color outline)
        {
            string text = known ? pct.ToString() : "?";
            float px = text.Length >= 3 ? size * 0.40f : size * 0.56f;
            Color fill = !known ? UnknownGray : (charging ? ChargingBlue : level);
            using (var font = new Font("Segoe UI", px, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var sf = new StringFormat())
            using (var back = new SolidBrush(outline))
            using (var fore = new SolidBrush(fill))
            {
                sf.Alignment = StringAlignment.Center;
                sf.LineAlignment = StringAlignment.Center;
                var rect = new RectangleF(0, 0, size, size);
                float d = Math.Max(1f, size / 32f);
                for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                        if (dx != 0 || dy != 0)
                            g.DrawString(text, font, back, new RectangleF(dx * d, dy * d, size, size), sf);
                g.DrawString(text, font, fore, rect, sf);
            }
        }

        static GraphicsPath RoundRect(RectangleF r, float radius)
        {
            var p = new GraphicsPath();
            float d = radius * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        static byte[] BuildIco(List<Bitmap> bitmaps)
        {
            var pngs = new List<byte[]>();
            foreach (var bmp in bitmaps)
            {
                using (var ms = new MemoryStream())
                {
                    bmp.Save(ms, ImageFormat.Png);
                    pngs.Add(ms.ToArray());
                }
            }
            using (var ms = new MemoryStream())
            {
                var w = new BinaryWriter(ms);
                w.Write((ushort)0);
                w.Write((ushort)1);
                w.Write((ushort)bitmaps.Count);
                int offset = 6 + 16 * bitmaps.Count;
                for (int i = 0; i < bitmaps.Count; i++)
                {
                    int s = bitmaps[i].Width;
                    w.Write((byte)(s >= 256 ? 0 : s));
                    w.Write((byte)(s >= 256 ? 0 : s));
                    w.Write((byte)0);
                    w.Write((byte)0);
                    w.Write((ushort)1);
                    w.Write((ushort)32);
                    w.Write((uint)pngs[i].Length);
                    w.Write((uint)offset);
                    offset += pngs[i].Length;
                }
                foreach (var png in pngs) w.Write(png);
                w.Flush();
                return ms.ToArray();
            }
        }

        /// <summary>Writes PNG previews of each indicator state (for visual checking).</summary>
        public static void WritePreviews(string dir)
        {
            Directory.CreateDirectory(dir);
            var charging = new Reading { Found = true, Known = true, Percent = 95, Wired = true, Millivolts = 4097 };
            var healthy = new Reading { Found = true, Known = true, Percent = 42, Wired = false, Millivolts = 3840 };
            var low = new Reading { Found = true, Known = true, Percent = 12, Wired = false, Millivolts = 3450 };
            var none = new Reading();
            SavePreview(dir, "battery-95-charging", charging, false, false);
            SavePreview(dir, "battery-42", healthy, false, false);
            SavePreview(dir, "battery-12", low, false, false);
            SavePreview(dir, "battery-95-light-taskbar", charging, false, true);
            SavePreview(dir, "digits-95-charging", charging, true, false);
            SavePreview(dir, "digits-42", healthy, true, false);
            SavePreview(dir, "digits-12", low, true, false);
            SavePreview(dir, "unknown", none, false, false);
        }

        static void SavePreview(string dir, string name, Reading r, bool digits, bool light)
        {
            using (var bmp = Render(128, r, digits, light))
                bmp.Save(Path.Combine(dir, name + ".png"), ImageFormat.Png);
        }

        /// <summary>Writes a 256px application icon (PNG-in-ICO) for the exe.</summary>
        public static void WriteAppIcon(string path)
        {
            var frames = new List<Bitmap>();
            try
            {
                foreach (int s in new[] { 16, 32, 48, 64, 128, 256 })
                {
                    var bmp = new Bitmap(s, s, PixelFormat.Format32bppArgb);
                    using (var g = Graphics.FromImage(bmp))
                    {
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        g.Clear(Color.Transparent);
                        using (var bg = new SolidBrush(Color.FromArgb(30, 41, 59)))
                        using (var bgPath = RoundRect(new RectangleF(0, 0, s, s), s * 0.22f))
                            g.FillPath(bg, bgPath);
                        g.ScaleTransform(s / 32f, s / 32f);
                        using (var pen = new Pen(Color.White, 2f))
                        using (var body = RoundRect(new RectangleF(5.5f, 10f, 18f, 12f), 2.2f))
                            g.DrawPath(pen, body);
                        using (var brush = new SolidBrush(Color.White))
                            g.FillRectangle(brush, 24f, 13f, 2.5f, 6f);
                        using (var fill = new SolidBrush(Green))
                            g.FillRectangle(fill, 8f, 12.5f, 11.5f, 7f);
                        PointF[] bolt =
                        {
                            new PointF(19f, 9f), new PointF(14f, 17f), new PointF(17f, 17f),
                            new PointF(15.5f, 23f), new PointF(21f, 14.5f), new PointF(18f, 14.5f)
                        };
                        using (var boltPath = new GraphicsPath())
                        {
                            boltPath.AddPolygon(bolt);
                            using (var b = new SolidBrush(Color.White)) g.FillPath(b, boltPath);
                        }
                    }
                    frames.Add(bmp);
                }
                File.WriteAllBytes(path, BuildIco(frames));
            }
            finally
            {
                foreach (var f in frames) f.Dispose();
            }
        }
    }
}
