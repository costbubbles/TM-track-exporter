using System.Text;
var mode = args[0];
if (mode == "kn5") Kn5(args[1], args.Length > 2 ? int.Parse(args[2]) : 99);
if (mode == "ai") Ai(args[1]);
if (mode == "aitail") Extra.AiTail(args[1], float.Parse(args[2]), float.Parse(args[3]));

static string Str(BinaryReader r) { var n = r.ReadInt32(); return Encoding.UTF8.GetString(r.ReadBytes(n)); }

static void Kn5(string path, int maxDepth)
{
    using var r = new BinaryReader(File.OpenRead(path));
    var magic = Encoding.ASCII.GetString(r.ReadBytes(6));
    var ver = r.ReadInt32();
    Console.WriteLine($"magic={magic} version={ver} fileLen={r.BaseStream.Length}");
    if (ver > 5) Console.WriteLine($"  v6 extra int = {r.ReadInt32()}");
    var tc = r.ReadInt32();
    Console.WriteLine($"textures={tc}");
    for (int i = 0; i < tc; i++) { var active = r.ReadInt32(); var name = Str(r); var size = r.ReadInt32(); var hdr = r.ReadBytes(Math.Min(size, 4)); r.BaseStream.Seek(size - hdr.Length, SeekOrigin.Current); if (i < 6) Console.WriteLine($"  tex active={active} {name} {size}B magic={Encoding.ASCII.GetString(hdr).Replace("\0", ".")}"); }
    var mc = r.ReadInt32();
    Console.WriteLine($"materials={mc}");
    var shaders = new Dictionary<string, int>();
    for (int i = 0; i < mc; i++)
    {
        var name = Str(r); var shader = Str(r); var blend = r.ReadByte(); var alphaTested = r.ReadBoolean(); var depth = r.ReadInt32();
        var pc = r.ReadInt32(); var props = new List<string>();
        for (int p = 0; p < pc; p++) { var pn = Str(r); var a = r.ReadSingle(); r.ReadBytes(36); props.Add($"{pn}={a}"); }
        var sc = r.ReadInt32(); var slots = new List<string>();
        for (int s = 0; s < sc; s++) { var sn = Str(r); var slot = r.ReadInt32(); var tn = Str(r); slots.Add($"{sn}#{slot}={tn}"); }
        shaders[shader] = shaders.GetValueOrDefault(shader) + 1;
        if (i < 4) Console.WriteLine($"  mat {name} shader={shader} blend={blend} at={alphaTested} depth={depth} props=[{string.Join(", ", props)}] slots=[{string.Join(", ", slots)}]");
    }
    Console.WriteLine("  shaders: " + string.Join(", ", shaders.OrderByDescending(k => k.Value).Select(k => $"{k.Key}x{k.Value}")));
    var stats = new Dictionary<string, int>();
    Node(r, 0, maxDepth, stats);
    Console.WriteLine($"end pos={r.BaseStream.Position} len={r.BaseStream.Length}");
    Console.WriteLine("node stats: " + string.Join(", ", stats.Select(k => $"{k.Key}={k.Value}")));
}

static void Node(BinaryReader r, int depth, int maxDepth, Dictionary<string, int> stats)
{
    var cls = r.ReadInt32(); var name = Str(r); var children = r.ReadInt32(); var active = r.ReadBoolean();
    string info = "";
    if (cls == 1)
    {
        var m = new float[16]; for (int i = 0; i < 16; i++) m[i] = r.ReadSingle();
        info = $"matrix row0=({m[0]:0.###},{m[1]:0.###},{m[2]:0.###},{m[3]:0.###}) row1=({m[4]:0.###},{m[5]:0.###},{m[6]:0.###}) row2=({m[8]:0.###},{m[9]:0.###},{m[10]:0.###}) t=({m[12]:0.##},{m[13]:0.##},{m[14]:0.##})";
    }
    else if (cls == 2)
    {
        var cast = r.ReadBoolean(); var vis = r.ReadBoolean(); var transp = r.ReadBoolean();
        var vc = r.ReadInt32(); r.BaseStream.Seek(vc * 44L, SeekOrigin.Current);
        var ic = r.ReadInt32(); r.BaseStream.Seek(ic * 2L, SeekOrigin.Current);
        var mat = r.ReadInt32(); var layer = r.ReadInt32(); var lodIn = r.ReadSingle(); var lodOut = r.ReadSingle();
        r.ReadBytes(12); var rad = r.ReadSingle(); var renderable = r.ReadBoolean();
        info = $"mesh v={vc} i={ic} mat={mat} cast={cast} vis={vis} transp={transp} layer={layer} lod={lodIn}-{lodOut} rad={rad:0.#} renderable={renderable}";
        stats[renderable ? "mesh_renderable" : "mesh_hidden"] = stats.GetValueOrDefault(renderable ? "mesh_renderable" : "mesh_hidden") + 1;
        stats["maxVerts"] = Math.Max(stats.GetValueOrDefault("maxVerts"), vc);
    }
    else throw new Exception($"node class {cls} at {r.BaseStream.Position}");
    stats["class" + cls] = stats.GetValueOrDefault("class" + cls) + 1;
    if (depth <= maxDepth && (name.StartsWith("AC_") || depth < 1 || (cls == 2 && name.Length > 0 && char.IsDigit(name[0]) && stats.GetValueOrDefault("printedPhys") < 12 && (stats["printedPhys"] = stats.GetValueOrDefault("printedPhys") + 1) > 0)))
        Console.WriteLine($"{new string(' ', depth * 2)}[{cls}] '{name}' children={children} active={active} {info}");
    for (int i = 0; i < children; i++) Node(r, depth + 1, maxDepth, stats);
}

static void Ai(string path)
{
    using var r = new BinaryReader(File.OpenRead(path));
    var len = r.BaseStream.Length;
    var h = new int[4]; for (int i = 0; i < 4; i++) h[i] = r.ReadInt32();
    Console.WriteLine($"len={len} header ints: {string.Join(",", h)}");
    var n = h[1];
    for (int i = 0; i < n; i++) { var x = r.ReadSingle(); var y = r.ReadSingle(); var z = r.ReadSingle(); var l = r.ReadSingle(); var id = r.ReadInt32(); if (i < 3 || i == n - 1) Console.WriteLine($"  p{i} pos=({x:0.##},{y:0.##},{z:0.##}) len={l:0.##} id={id}"); }
    var pos = r.BaseStream.Position; var extraCount = r.ReadInt32();
    var remaining = len - pos - 4;
    Console.WriteLine($"after points pos={pos} extraCount={extraCount} remaining={remaining} bytes/extra={(extraCount > 0 ? remaining / (double)extraCount : 0):0.##} floats/extra={(extraCount > 0 ? remaining / 4.0 / extraCount : 0):0.##}");
    for (int i = 0; i < Math.Min(2, extraCount); i++) { var f = new float[18]; for (int k = 0; k < 18; k++) f[k] = r.ReadSingle(); Console.WriteLine("  extra" + i + ": " + string.Join(" ", f.Select(v => v.ToString("0.###")))); }
    var rest = len - r.BaseStream.Position;
    Console.WriteLine($"bytes after 2 extras: {rest}");
}

static class Extra
{
    public static void AiTail(string path, float qx, float qz)
    {
        using var r = new BinaryReader(File.OpenRead(path));
        r.ReadInt32(); var n = r.ReadInt32(); r.ReadInt32(); r.ReadInt32();
        var pts = new (float x, float y, float z)[n];
        for (int i = 0; i < n; i++) { pts[i] = (r.ReadSingle(), r.ReadSingle(), r.ReadSingle()); r.ReadSingle(); r.ReadInt32(); }
        var best = 0; var bd = float.MaxValue;
        for (int i = 0; i < n; i++) { var d = (pts[i].x - qx) * (pts[i].x - qx) + (pts[i].z - qz) * (pts[i].z - qz); if (d < bd) { bd = d; best = i; } }
        var a = pts[best]; var b = pts[(best + 1) % n];
        Console.WriteLine($"nearest p{best} at ({a.x:0.##},{a.z:0.##}) dist={MathF.Sqrt(bd):0.##} dir=({b.x - a.x:0.###},{b.z - a.z:0.###})");
        var ec = r.ReadInt32(); r.BaseStream.Seek(ec * 72L, SeekOrigin.Current);
        Console.WriteLine($"after extras pos={r.BaseStream.Position} len={r.BaseStream.Length}");
        var ints = new List<string>(); for (int i = 0; i < 12 && r.BaseStream.Position + 4 <= r.BaseStream.Length; i++) { var p = r.BaseStream.Position; var iv = r.ReadInt32(); r.BaseStream.Position = p; var fv = r.ReadSingle(); ints.Add($"{iv}/{fv:0.###}"); }
        Console.WriteLine("tail as int/float: " + string.Join("  ", ints));
    }
}
