using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Reflection;
using System.Xml.Linq;

namespace MineImatorSimplyRemade.core;

/// <summary>
/// Small software SVG rasterizer for icon SVGs.
/// Supports filled paths, including lines, cubic/quadratic Béziers and arcs.
/// Produces a white-on-transparent RGBA byte array (row 0 = top) suitable
/// for flipping and uploading as an OpenGL texture.
/// </summary>
public static class SvgLoader
{
    public struct SvgImage
    {
        /// <summary>RGBA bytes, row-major, row 0 = top of image.</summary>
        public byte[] Data;
        public int    Width;
        public int    Height;
    }

    /// <summary>Loads and rasterizes an embedded SVG asset by its manifest resource name.</summary>
    public static SvgImage LoadEmbedded(string resourceName, int size = 20)
    {
        var asm    = Assembly.GetExecutingAssembly();
        using var s = asm.GetManifestResourceStream(resourceName)
            ?? throw new FileNotFoundException($"Embedded resource not found: {resourceName}");
        return Load(s, size);
    }

    /// <summary>Rasterizes an SVG from a stream to a square, antialiased RGBA bitmap.</summary>
    public static SvgImage Load(Stream stream, int size = 20)
    {
        var doc  = XDocument.Load(stream);
        var root = doc.Root!;

        float vbX = 0f, vbY = 0f, vbW = 24f, vbH = 24f;
        var vb = root.Attribute("viewBox")?.Value;
        if (vb != null)
        {
            var p = vb.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
            if (p.Length >= 4)
            {
                float.TryParse(p[0], NumberStyles.Float, CultureInfo.InvariantCulture, out vbX);
                float.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out vbY);
                float.TryParse(p[2], NumberStyles.Float, CultureInfo.InvariantCulture, out vbW);
                float.TryParse(p[3], NumberStyles.Float, CultureInfo.InvariantCulture, out vbH);
            }
        }

        if (size <= 0) throw new ArgumentOutOfRangeException(nameof(size));
        if (vbW <= 0f || vbH <= 0f) throw new InvalidDataException("SVG viewBox must have a positive size.");

        const int samples = 4;
        int rasterSize = checked(size * samples);
        float scale = MathF.Min(rasterSize / vbW, rasterSize / vbH);
        float ox = (rasterSize - vbW * scale) * 0.5f;
        float oy = (rasterSize - vbH * scale) * 0.5f;
        var raster = new byte[rasterSize * rasterSize];

        foreach (var elem in root.Descendants())
        {
            if (elem.Name.LocalName != "path") continue;
            var d = elem.Attribute("d")?.Value;
            if (string.IsNullOrWhiteSpace(d)) continue;

            var scaled = ParsePath(d).Where(poly => poly.Count >= 2).Select(poly => poly.ConvertAll(pt =>
                new Vector2((pt.X - vbX) * scale + ox, (pt.Y - vbY) * scale + oy))).ToList();
            FillPath(raster, rasterSize, rasterSize, scaled);
        }

        var pixels = new byte[size * size * 4];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            int covered = 0;
            for (int yy = 0; yy < samples; yy++)
            for (int xx = 0; xx < samples; xx++)
                covered += raster[(y * samples + yy) * rasterSize + x * samples + xx];

            byte alpha = (byte)((covered + samples * samples / 2) / (samples * samples));
            int idx = (y * size + x) * 4;
            pixels[idx] = pixels[idx + 1] = pixels[idx + 2] = 255;
            pixels[idx + 3] = alpha;
        }
        return new SvgImage { Data = pixels, Width = size, Height = size };
    }

    // ── SVG path parser ───────────────────────────────────────────────────────

    private static List<List<Vector2>> ParsePath(string d)
    {
        var result = new List<List<Vector2>>();
        var sub    = new List<Vector2>();
        float px = 0f, py = 0f, startX = 0f, startY = 0f;
        var lastControl = Vector2.Zero;
        char previousCmd = ' ';
        char  cmd = ' ';
        int   pos = 0, len = d.Length;

        void CommitSub()
        {
            if (sub.Count >= 2) result.Add([..sub]);
            sub.Clear();
        }

        void SkipSep()
        {
            while (pos < len && d[pos] is ' ' or ',' or '\t' or '\r' or '\n') pos++;
        }

        float Num()
        {
            SkipSep();
            int s = pos;
            if (pos < len && d[pos] is '+' or '-') pos++;

            while (pos < len && char.IsDigit(d[pos])) pos++;
            if (pos < len && d[pos] == '.')
            {
                pos++;
                while (pos < len && char.IsDigit(d[pos])) pos++;
            }

            if (pos < len && d[pos] is 'e' or 'E')
            {
                int exponent = pos++;
                if (pos < len && d[pos] is '+' or '-') pos++;
                int exponentDigits = pos;
                while (pos < len && char.IsDigit(d[pos])) pos++;
                if (pos == exponentDigits) pos = exponent;
            }
            return float.Parse(d.AsSpan(s, pos - s), NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        bool PeekNum()
        {
            int j = pos;
            while (j < len && d[j] is ' ' or ',' or '\t' or '\r' or '\n') j++;
            return j < len && (d[j] is '+' or '-' or '.' || char.IsDigit(d[j]));
        }

        while (pos < len)
        {
            SkipSep();
            if (pos >= len) break;

            if (char.IsLetter(d[pos]))
                cmd = d[pos++];
            else if (cmd == ' ')
            {
                pos++; // skip unexpected non-letter / non-number
                continue;
            }

            if (cmd is 'Z' or 'z')
            {
                if (sub.Count > 0) sub.Add(new Vector2(startX, startY));
                CommitSub();
                px = startX; py = startY;
                cmd = ' ';
                continue;
            }

            if (!PeekNum()) continue;

            char activeCmd = cmd;
            switch (cmd)
            {
                case 'M':
                    px = Num(); py = Num(); startX = px; startY = py;
                    CommitSub();
                    sub.Add(new Vector2(px, py));
                    cmd = 'L';
                    break;
                case 'm':
                    px += Num(); py += Num(); startX = px; startY = py;
                    CommitSub();
                    sub.Add(new Vector2(px, py));
                    cmd = 'l';
                    break;
                case 'L': px = Num(); py = Num(); sub.Add(new Vector2(px, py)); break;
                case 'l': px += Num(); py += Num(); sub.Add(new Vector2(px, py)); break;
                case 'H': px  = Num(); sub.Add(new Vector2(px, py)); break;
                case 'h': px += Num(); sub.Add(new Vector2(px, py)); break;
                case 'V': py  = Num(); sub.Add(new Vector2(px, py)); break;
                case 'v': py += Num(); sub.Add(new Vector2(px, py)); break;
                case 'C':
                case 'c':
                {
                    bool relative = cmd == 'c';
                    var c1 = ReadPoint(relative); var c2 = ReadPoint(relative); var end = ReadPoint(relative);
                    AddCubic(sub, new Vector2(px, py), c1, c2, end);
                    px = end.X; py = end.Y; lastControl = c2;
                    break;
                }
                case 'S':
                case 's':
                {
                    bool relative = cmd == 's';
                    var start = new Vector2(px, py);
                    var c1 = previousCmd is 'C' or 'c' or 'S' or 's' ? start * 2f - lastControl : start;
                    var c2 = ReadPoint(relative); var end = ReadPoint(relative);
                    AddCubic(sub, start, c1, c2, end);
                    px = end.X; py = end.Y; lastControl = c2;
                    break;
                }
                case 'Q':
                case 'q':
                {
                    bool relative = cmd == 'q';
                    var control = ReadPoint(relative); var end = ReadPoint(relative);
                    AddQuadratic(sub, new Vector2(px, py), control, end);
                    px = end.X; py = end.Y; lastControl = control;
                    break;
                }
                case 'T':
                case 't':
                {
                    bool relative = cmd == 't';
                    var start = new Vector2(px, py);
                    var control = previousCmd is 'Q' or 'q' or 'T' or 't' ? start * 2f - lastControl : start;
                    var end = ReadPoint(relative);
                    AddQuadratic(sub, start, control, end);
                    px = end.X; py = end.Y; lastControl = control;
                    break;
                }
                case 'A':
                {
                    float rx = Num(), ry = Num(), xr = Num();
                    bool  la = Num() != 0f, sw = Num() != 0f;
                    float ex = Num(), ey = Num();
                    ArcToPolyline(sub, px, py, rx, ry, xr * MathF.PI / 180f, la, sw, ex, ey);
                    px = ex; py = ey;
                    break;
                }
                case 'a':
                {
                    float rx = Num(), ry = Num(), xr = Num();
                    bool  la = Num() != 0f, sw = Num() != 0f;
                    float ex = px + Num(), ey = py + Num();
                    ArcToPolyline(sub, px, py, rx, ry, xr * MathF.PI / 180f, la, sw, ex, ey);
                    px = ex; py = ey;
                    break;
                }
                default:
                    cmd = ' ';
                    break;
            }
            previousCmd = activeCmd;
        }

        CommitSub();
        return result;

        Vector2 ReadPoint(bool relative)
        {
            float x = Num(), y = Num();
            return relative ? new Vector2(px + x, py + y) : new Vector2(x, y);
        }
    }

    private static void AddCubic(List<Vector2> poly, Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3)
    {
        const int steps = 16;
        for (int i = 1; i <= steps; i++)
        {
            float t = i / (float)steps, u = 1f - t;
            poly.Add(u * u * u * p0 + 3f * u * u * t * p1 + 3f * u * t * t * p2 + t * t * t * p3);
        }
    }

    private static void AddQuadratic(List<Vector2> poly, Vector2 p0, Vector2 p1, Vector2 p2)
    {
        const int steps = 12;
        for (int i = 1; i <= steps; i++)
        {
            float t = i / (float)steps, u = 1f - t;
            poly.Add(u * u * p0 + 2f * u * t * p1 + t * t * p2);
        }
    }

    // ── SVG arc → polyline ────────────────────────────────────────────────────

    private static void ArcToPolyline(List<Vector2> poly,
        float x1, float y1, float rx, float ry, float phi,
        bool largeArc, bool sweep, float x2, float y2)
    {
        if (rx == 0f || ry == 0f) { poly.Add(new Vector2(x2, y2)); return; }

        float cosPhi = MathF.Cos(phi), sinPhi = MathF.Sin(phi);
        float dx = (x1 - x2) / 2f, dy = (y1 - y2) / 2f;
        float x1p =  cosPhi * dx + sinPhi * dy;
        float y1p = -sinPhi * dx + cosPhi * dy;

        // Ensure radii are large enough
        float lam = (x1p * x1p) / (rx * rx) + (y1p * y1p) / (ry * ry);
        if (lam > 1f) { float s = MathF.Sqrt(lam); rx *= s; ry *= s; }

        float rxSq = rx * rx, rySq = ry * ry;
        float x1pSq = x1p * x1p, y1pSq = y1p * y1p;
        float num = MathF.Max(0f, rxSq * rySq - rxSq * y1pSq - rySq * x1pSq);
        float den = rxSq * y1pSq + rySq * x1pSq;
        float sq  = den > 0f ? MathF.Sqrt(num / den) : 0f;
        if (largeArc == sweep) sq = -sq;

        float cxp = sq * rx * y1p / ry;
        float cyp = -sq * ry * x1p / rx;
        float cx  = cosPhi * cxp - sinPhi * cyp + (x1 + x2) / 2f;
        float cy  = sinPhi * cxp + cosPhi * cyp + (y1 + y2) / 2f;

        float ux = (x1p - cxp) / rx, uy = (y1p - cyp) / ry;
        float vx = (-x1p - cxp) / rx, vy = (-y1p - cyp) / ry;

        float theta  = VecAngle(1f, 0f, ux, uy);
        float dTheta = VecAngle(ux, uy, vx, vy);
        if (!sweep && dTheta > 0f) dTheta -= MathF.Tau;
        if ( sweep && dTheta < 0f) dTheta += MathF.Tau;

        int steps = Math.Max(16, (int)(MathF.Abs(dTheta) * MathF.Max(rx, ry)));
        for (int i = 1; i <= steps; i++)
        {
            float t = theta + dTheta * i / steps;
            poly.Add(new Vector2(
                cosPhi * MathF.Cos(t) * rx - sinPhi * MathF.Sin(t) * ry + cx,
                sinPhi * MathF.Cos(t) * rx + cosPhi * MathF.Sin(t) * ry + cy));
        }
    }

    private static float VecAngle(float ux, float uy, float vx, float vy)
    {
        float dot = ux * vx + uy * vy;
        float mag = MathF.Sqrt((ux * ux + uy * uy) * (vx * vx + vy * vy));
        float a   = MathF.Acos(Math.Clamp(dot / mag, -1f, 1f));
        return (ux * vy - uy * vx) < 0f ? -a : a;
    }

    // ── Scanline fill (even-odd rule) ─────────────────────────────────────────

    private static void FillPath(byte[] pixels, int w, int h, List<List<Vector2>> polygons)
    {
        for (int y = 0; y < h; y++)
        {
            float fy = y + 0.5f;
            var xs = new List<float>(16);

            foreach (var poly in polygons)
            {
                int n = poly.Count;
                for (int j = 0, k = n - 1; j < n; k = j++)
                {
                    float yj = poly[j].Y, yk = poly[k].Y;
                    if ((yj <= fy && yk > fy) || (yk <= fy && yj > fy))
                        xs.Add(poly[k].X + (fy - yk) * (poly[j].X - poly[k].X) / (yj - yk));
                }
            }

            xs.Sort();
            for (int p = 0; p + 1 < xs.Count; p += 2)
            {
                int x0 = Math.Max(0,     (int)MathF.Ceiling(xs[p]));
                int x1 = Math.Min(w - 1, (int)MathF.Floor  (xs[p + 1]));
                for (int x = x0; x <= x1; x++)
                {
                    pixels[y * w + x] = 255;
                }
            }
        }
    }
}
