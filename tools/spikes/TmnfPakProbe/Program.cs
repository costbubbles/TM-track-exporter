using GBX.NET; using GBX.NET.PAK; using GBX.NET.Engines.Plug; using SkiaSharp;
Gbx.LZO = new GBX.NET.LZO.Lzo(); Gbx.ZLib = new GBX.NET.ZLib.ZLib();
var tm = args[0]; var refPath = args[1]; var outDir = args[2];
var list = PakList.Parse(Path.Combine(tm, "Packs", "packlist.dat"), PakListGame.TM);
using var fs = File.OpenRead(Path.Combine(tm, "Packs", "Stadium.pak"));
var pak = Pak.Parse(fs, list["stadium"].GetBytes(), KeyType.BaseKey);

PakFile? Resolve(string fullPath)
{
    if (pak.Files.TryGetValue(fullPath, out var f)) return f;
    var dir = Path.GetDirectoryName(fullPath); var name = Path.GetFileName(fullPath);
    while (!string.IsNullOrEmpty(dir))
    {
        if (pak.Files.TryGetValue(dir + "\\" + GBX.NET.Crypto.MD5.Compute136(name), out f)) return f;
        name = Path.GetFileName(dir) + "\\" + name; dir = Path.GetDirectoryName(dir);
    }
    return null;
}

if (refPath == "blocks")
{
    // Mode "blocks": list every block info in the pak with its WayPointType (used to build data/block-flags.tmnf.json).
    var lines = new List<string>();
    foreach (var file in pak.Files.Values.Where(f => f.FolderPath.Contains("ConstructionBlockInfo", StringComparison.Ordinal)))
    {
        try
        {
            var node = pak.OpenGbxFile(file, new GbxReadSettings(), false, null!).Node;
            if (node is GBX.NET.Engines.Game.CGameCtnBlockInfo info)
            {
                lines.Add($"{info.Ident.Id}\t{info.GetType().Name}\t{info.WayPointType}");
            }
        }
        catch (Exception e)
        {
            lines.Add($"{file.Name}\tERROR\t{e.GetType().Name}");
        }
    }

    lines.Sort(StringComparer.Ordinal);
    File.WriteAllLines(outDir, lines);
    Console.WriteLine($"{lines.Count} block infos; waypoint counts: " + string.Join(", ", lines.Select(l => l.Split('\t')[2]).GroupBy(x => x).Select(g => $"{g.Key}={g.Count()}")));
    return;
}

var solid = (CPlugSolid)pak.OpenGbxFile(Resolve(refPath)!, new GbxReadSettings(), true, null!).Node!;
solid.ExportToObj(Path.Combine(outDir, "block.obj"), Path.Combine(outDir, "block.mtl"), lod: 0);
Console.WriteLine($"OBJ written: {new FileInfo(Path.Combine(outDir, "block.obj")).Length} bytes");

// Top-down render: visual LOD0 (grey fill per material colour) + collision mesh (red wire)
var tris = new List<(SKPoint a, SKPoint b, SKPoint c, int mat)>();
var mats = new List<string>();
float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
int vcount = 0;
foreach (var (tree, loc) in solid.GetAllChildrenWithLocation(0))
{
    if (tree.Visual is not CPlugVisualIndexedTriangles vis || vis.IndexBuffer is null) continue;
    var mname = tree.MaterialFile?.FilePath ?? "?"; var mi = mats.IndexOf(mname); if (mi < 0) { mats.Add(mname); mi = mats.Count - 1; }
    SKPoint P(int i) { var v = vis.Vertices[i].Position; var x = loc.XX * v.X + loc.XY * v.Y + loc.XZ * v.Z + loc.TX; var z = loc.ZX * v.X + loc.ZY * v.Y + loc.ZZ * v.Z + loc.TZ; minX = Math.Min(minX, x); maxX = Math.Max(maxX, x); minZ = Math.Min(minZ, z); maxZ = Math.Max(maxZ, z); return new SKPoint(x, z); }
    var idx = vis.IndexBuffer.Indices; vcount += vis.Vertices.Length;
    for (int i = 0; i + 2 < idx.Length; i += 3) tris.Add((P(idx[i]), P(idx[i + 1]), P(idx[i + 2]), mi));
}
Console.WriteLine($"visual LOD0: {tris.Count} tris, {vcount} verts, materials: {string.Join(", ", mats.Select(Path.GetFileNameWithoutExtension))}");
Console.WriteLine($"visual XZ bounds: x {minX:0.#}..{maxX:0.#} z {minZ:0.#}..{maxZ:0.#}");

var coll = new List<(SKPoint, SKPoint, SKPoint, int)>();
var surfNode = (CPlugSurface)((CPlugTree)solid.Tree!).Surface!;
if (surfNode.Geom?.Surf is CPlugSurface.Mesh mesh)
{
    foreach (var t in mesh.CookedTriangles!) coll.Add((new(mesh.Vertices[t.Indices.X].X, mesh.Vertices[t.Indices.X].Z), new(mesh.Vertices[t.Indices.Y].X, mesh.Vertices[t.Indices.Y].Z), new(mesh.Vertices[t.Indices.Z].X, mesh.Vertices[t.Indices.Z].Z), t.SurfaceIndex));
    var surfIds = surfNode.Materials!.Select(m => m.Material?.SurfaceId.ToString() ?? m.MaterialFile?.FilePath ?? "?").ToList();
    Console.WriteLine($"collision: {coll.Count} tris; x {mesh.Vertices.Min(v => v.X):0.#}..{mesh.Vertices.Max(v => v.X):0.#} z {mesh.Vertices.Min(v => v.Z):0.#}..{mesh.Vertices.Max(v => v.Z):0.#}");
    Console.WriteLine("collision surfaces by triangle: " + string.Join(", ", coll.GroupBy(c => c.Item4).Select(g => $"{(g.Key < surfIds.Count ? surfIds[g.Key] : g.Key.ToString())}x{g.Count()}")));
}

const int W = 900; float sc = (W - 40) / Math.Max(maxX - minX, maxZ - minZ);
using var bmp = new SKBitmap(W, W); using var cv = new SKCanvas(bmp); cv.Clear(SKColors.White);
SKPoint M(SKPoint p) => new(20 + (p.X - minX) * sc, 20 + (p.Y - minZ) * sc);
var palette = new[] { SKColors.SteelBlue, SKColors.Orange, SKColors.SeaGreen, SKColors.MediumPurple, SKColors.Goldenrod, SKColors.IndianRed, SKColors.Teal, SKColors.SlateGray, SKColors.Olive, SKColors.Sienna, SKColors.DarkCyan, SKColors.Plum };
foreach (var (a, b, c, m) in tris) { using var path = new SKPath(); path.MoveTo(M(a)); path.LineTo(M(b)); path.LineTo(M(c)); path.Close(); using var paint = new SKPaint { Color = palette[m % palette.Length].WithAlpha(140), Style = SKPaintStyle.Fill }; cv.DrawPath(path, paint); }
using (var red = new SKPaint { Color = SKColors.Red.WithAlpha(90), Style = SKPaintStyle.Stroke, StrokeWidth = 1 })
    foreach (var (a, b, c, _) in coll) { cv.DrawLine(M(a), M(b), red); cv.DrawLine(M(b), M(c), red); cv.DrawLine(M(c), M(a), red); }
using var data = bmp.Encode(SKEncodedImageFormat.Png, 90); File.WriteAllBytes(Path.Combine(outDir, "block_topdown.png"), data.ToArray());

// Texture: extract the diffuse DDS for the turbo material
var tex = Resolve(@"Stadium\Media\Texture\Image\StadiumRoadTurboD.dds");
if (tex != null) { using var s = pak.OpenFile(tex, out _); using var ms = new MemoryStream(); s.CopyTo(ms); var b = ms.ToArray(); File.WriteAllBytes(Path.Combine(outDir, "StadiumRoadTurboD.dds"), b); Console.WriteLine($"DDS: {b.Length} bytes, magic={System.Text.Encoding.ASCII.GetString(b, 0, 4)} size={BitConverter.ToInt32(b, 16)}x{BitConverter.ToInt32(b, 12)} fourCC={System.Text.Encoding.ASCII.GetString(b, 84, 4)}"); }
else Console.WriteLine("DDS not resolved");
