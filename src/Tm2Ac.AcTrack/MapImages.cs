using System.Numerics;
using SkiaSharp;
using Tm2Ac.AcTrack.Ini;
using Tm2Ac.Geometry;

namespace Tm2Ac.AcTrack;

/// <summary>
/// data/map.ini parameters. Verified against Kunos and community tracks: WIDTH/HEIGHT are map.png's pixel size,
/// offsets are in metres, and <c>pixel = (world + OFFSET) / SCALE_FACTOR</c> (image down = +Z).
/// </summary>
public sealed record MapLayout(int Width, int Height, float Margin, float ScaleFactor, float XOffset, float ZOffset)
{
    public const int MaxSize = 1600;
    public const int DrawingSize = 10;

    public static MapLayout FromBounds(float minX, float maxX, float minZ, float maxZ, float margin = 20)
    {
        var worldWidth = maxX - minX + (2 * margin);
        var worldHeight = maxZ - minZ + (2 * margin);
        var scale = Math.Max(1f, Math.Max(worldWidth, worldHeight) / MaxSize);
        return new MapLayout(
            (int)MathF.Ceiling(worldWidth / scale),
            (int)MathF.Ceiling(worldHeight / scale),
            margin,
            scale,
            -minX + margin,
            -minZ + margin);
    }

    public static MapLayout FromPath(IEnumerable<Vector3> points, float margin = 20)
    {
        var list = points.ToList();
        return FromBounds(list.Min(p => p.X), list.Max(p => p.X), list.Min(p => p.Z), list.Max(p => p.Z), margin);
    }

    public Vector2 ToPixel(Vector3 world) => new((world.X + XOffset) / ScaleFactor, (world.Z + ZOffset) / ScaleFactor);

    public IniFile ToIni()
    {
        var ini = new IniFile();
        ini.Section("PARAMETERS")
            .Set("WIDTH", Width)
            .Set("HEIGHT", Height)
            .Set("MARGIN", Margin)
            .Set("SCALE_FACTOR", ScaleFactor)
            .Set("MAX_SIZE", MaxSize)
            .Set("X_OFFSET", XOffset)
            .Set("Z_OFFSET", ZOffset)
            .Set("DRAWING_SIZE", DrawingSize);
        return ini;
    }
}

public static class MapImages
{
    public const int OutlineWidth = 365;
    public const int OutlineHeight = 192;
    public const int PreviewWidth = 355;
    public const int PreviewHeight = 200;

    /// <summary>map.png: white line with a dark edge on transparent background, in <paramref name="layout"/> pixel space.</summary>
    public static byte[] RenderMap(Centerline path, MapLayout layout)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(layout);
        using var bitmap = new SKBitmap(layout.Width, layout.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        using var skPath = ToSkPath(path, p => layout.ToPixel(p));
        DrawLine(canvas, skPath, MapLayout.DrawingSize, SKColors.Black.WithAlpha(200), SKColors.White);
        return Encode(bitmap);
    }

    /// <summary>ui/outline.png: the track shape fitted into Content Manager's 365×192 outline box.</summary>
    public static byte[] RenderOutline(Centerline path)
    {
        using var bitmap = new SKBitmap(OutlineWidth, OutlineHeight, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        using var skPath = ToSkPath(path, Fit(path, OutlineWidth, OutlineHeight, padding: 12));
        DrawLine(canvas, skPath, 4, SKColors.Black.WithAlpha(160), SKColors.White);
        return Encode(bitmap);
    }

    /// <summary>ui/preview.png placeholder: dark card with the track shape and name.</summary>
    public static byte[] RenderPreview(Centerline path, string title)
    {
        using var bitmap = new SKBitmap(PreviewWidth, PreviewHeight);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(new SKColor(0x1E, 0x24, 0x2C));
        using var skPath = ToSkPath(path, Fit(path, PreviewWidth, PreviewHeight - 36, padding: 16));
        DrawLine(canvas, skPath, 6, new SKColor(0x10, 0x10, 0x10), new SKColor(0xF2, 0xB1, 0x34));
        using var font = new SKFont(SKTypeface.Default, 18);
        using var paint = new SKPaint { Color = SKColors.White, IsAntialias = true };
        canvas.DrawText(title, 12, PreviewHeight - 12, SKTextAlign.Left, font, paint);
        return Encode(bitmap);
    }

    private static Func<Vector3, Vector2> Fit(Centerline path, int width, int height, float padding)
    {
        var minX = path.Points.Min(p => p.X);
        var maxX = path.Points.Max(p => p.X);
        var minZ = path.Points.Min(p => p.Z);
        var maxZ = path.Points.Max(p => p.Z);
        var scale = Math.Min((width - (2 * padding)) / Math.Max(maxX - minX, 1), (height - (2 * padding)) / Math.Max(maxZ - minZ, 1));
        var offsetX = (width - ((maxX - minX) * scale)) / 2;
        var offsetY = (height - ((maxZ - minZ) * scale)) / 2;
        return p => new Vector2(offsetX + ((p.X - minX) * scale), offsetY + ((p.Z - minZ) * scale));
    }

    private static SKPath ToSkPath(Centerline path, Func<Vector3, Vector2> project)
    {
        using var builder = new SKPathBuilder();
        for (var i = 0; i < path.Count; i++)
        {
            var p = project(path.Points[i]);
            if (i == 0)
            {
                builder.MoveTo(p.X, p.Y);
            }
            else
            {
                builder.LineTo(p.X, p.Y);
            }
        }

        if (path.IsClosed)
        {
            builder.Close();
        }

        return builder.Detach();
    }

    private static void DrawLine(SKCanvas canvas, SKPath path, float width, SKColor edge, SKColor fill)
    {
        using var edgePaint = new SKPaint { Color = edge, Style = SKPaintStyle.Stroke, StrokeWidth = width + 3, IsAntialias = true, StrokeJoin = SKStrokeJoin.Round, StrokeCap = SKStrokeCap.Round };
        using var fillPaint = new SKPaint { Color = fill, Style = SKPaintStyle.Stroke, StrokeWidth = width, IsAntialias = true, StrokeJoin = SKStrokeJoin.Round, StrokeCap = SKStrokeCap.Round };
        canvas.DrawPath(path, edgePaint);
        canvas.DrawPath(path, fillPaint);
    }

    private static byte[] Encode(SKBitmap bitmap)
    {
        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
