using System.Numerics;

namespace Tm2Ac.Geometry;

/// <summary>Indexed triangle mesh in AC space. Normals and UVs are per vertex and may be left empty for collision meshes.</summary>
public sealed class MeshData
{
    public List<Vector3> Positions { get; } = [];
    public List<Vector3> Normals { get; } = [];
    public List<Vector2> Uvs { get; } = [];
    public List<int> Indices { get; } = [];

    public int VertexCount => Positions.Count;
    public int TriangleCount => Indices.Count / 3;
    public bool HasNormals => Normals.Count == Positions.Count;
    public bool HasUvs => Uvs.Count == Positions.Count;

    public int AddVertex(Vector3 position, Vector3 normal, Vector2 uv)
    {
        Positions.Add(position);
        Normals.Add(normal);
        Uvs.Add(uv);
        return Positions.Count - 1;
    }

    public void AddTriangle(int a, int b, int c)
    {
        Indices.Add(a);
        Indices.Add(b);
        Indices.Add(c);
    }

    /// <summary>Adds quad a-b-c-d (in order around its edge) as two triangles with the same winding.</summary>
    public void AddQuad(int a, int b, int c, int d)
    {
        AddTriangle(a, b, c);
        AddTriangle(a, c, d);
    }

    public void Append(MeshData other)
    {
        ArgumentNullException.ThrowIfNull(other);
        var offset = Positions.Count;
        Positions.AddRange(other.Positions);
        Normals.AddRange(other.Normals);
        Uvs.AddRange(other.Uvs);
        foreach (var i in other.Indices)
        {
            Indices.Add(i + offset);
        }
    }

    public (Vector3 Min, Vector3 Max) Bounds()
    {
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (var p in Positions)
        {
            min = Vector3.Min(min, p);
            max = Vector3.Max(max, p);
        }

        return (min, max);
    }

    /// <summary>Bounding sphere centred on the AABB centre (good enough for culling).</summary>
    public (Vector3 Center, float Radius) BoundingSphere()
    {
        if (Positions.Count == 0)
        {
            return (Vector3.Zero, 0);
        }

        var (min, max) = Bounds();
        var center = (min + max) / 2;
        var radius = Positions.Max(p => Vector3.Distance(center, p));
        return (center, radius);
    }

    /// <summary>
    /// Splits the mesh into pieces with at most <paramref name="maxVertices"/> vertices each, keeping whole triangles.
    /// Returns this instance unchanged (as the single piece) when it already fits.
    /// </summary>
    public IReadOnlyList<MeshData> Split(int maxVertices)
    {
        if (maxVertices < 3)
        {
            throw new ArgumentOutOfRangeException(nameof(maxVertices), "A mesh piece needs room for at least one triangle.");
        }

        if (VertexCount <= maxVertices)
        {
            return [this];
        }

        var pieces = new List<MeshData>();
        var current = new MeshData();
        var remap = new Dictionary<int, int>();

        for (var t = 0; t < Indices.Count; t += 3)
        {
            var newVertices = 0;
            for (var k = 0; k < 3; k++)
            {
                if (!remap.ContainsKey(Indices[t + k]))
                {
                    newVertices++;
                }
            }

            if (current.VertexCount + newVertices > maxVertices)
            {
                pieces.Add(current);
                current = new MeshData();
                remap.Clear();
            }

            for (var k = 0; k < 3; k++)
            {
                var source = Indices[t + k];
                if (!remap.TryGetValue(source, out var target))
                {
                    target = current.Positions.Count;
                    current.Positions.Add(Positions[source]);
                    if (HasNormals)
                    {
                        current.Normals.Add(Normals[source]);
                    }

                    if (HasUvs)
                    {
                        current.Uvs.Add(Uvs[source]);
                    }

                    remap[source] = target;
                }

                current.Indices.Add(target);
            }
        }

        if (current.Indices.Count > 0)
        {
            pieces.Add(current);
        }

        return pieces;
    }
}
