using SkiaSharp;
using Tm2Ac.AcTrack;
using Tm2Ac.Assets;

namespace Tm2Ac.Pipeline;

/// <summary>
/// Turns TMNF materials into KN5 materials. TM shaders are multi-texture; AC gets the diffuse map (or the best stand-in)
/// with ksPerPixel, alpha-tested for fences and "DiffA" shaders. Additive/night glow and fake-shadow materials are dropped.
/// Rules are based on the base shaders seen in Stadium.pak (Phase 3 survey).
/// </summary>
public sealed class MaterialTranslator(Func<string, byte[]?> readTexture)
{
    /// <summary>Slots tried in order when a material has no "Diffuse" texture.</summary>
    private static readonly string[] DiffuseSlots = ["Diffuse", "Advert", "Blend1", "GrassPC0", "SoilFix", "Soil", "SoilGenPC0", "FenceA", "Stripe", "Glow", "Blend2"];

    private const string FallbackTexture = "tm2ac_flat_grey.png";
    private const string WaterTexture = "tm2ac_water.png";

    private readonly Dictionary<string, AcMaterial?> _materials = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, AcTexture> _textures = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Textures referenced by translated materials, to embed in the KN5.</summary>
    public IEnumerable<AcTexture> Textures => _textures.Values;

    /// <summary>Materials translated so far (excluding dropped ones).</summary>
    public IEnumerable<AcMaterial> Materials => _materials.Values.OfType<AcMaterial>();

    public List<string> Warnings { get; } = [];

    /// <summary>KN5 material for a TM material, or null when the material shouldn't be rendered in AC.</summary>
    public AcMaterial? Translate(string materialPath, ExtractedMaterial? material)
    {
        if (_materials.TryGetValue(materialPath, out var cached))
        {
            return cached;
        }

        var result = Build(materialPath, material);
        _materials[materialPath] = result;
        return result;
    }

    public static bool IsDropped(string? baseShader) =>
        baseShader is not null && (baseShader.Contains("TAdd", StringComparison.Ordinal)
            || baseShader.Contains(" Add", StringComparison.Ordinal)
            || baseShader.Contains("ShadowSkirt", StringComparison.Ordinal));

    public static bool IsAlphaTested(string? baseShader) =>
        baseShader is not null && (baseShader.Contains("DiffA", StringComparison.Ordinal) || baseShader.Contains("Fence", StringComparison.Ordinal));

    private AcMaterial? Build(string materialPath, ExtractedMaterial? material)
    {
        var name = MaterialName(materialPath);
        if (material is not null && IsDropped(material.BaseShader))
        {
            return null;
        }

        if (material?.BaseShader?.StartsWith("Sea", StringComparison.Ordinal) == true)
        {
            AddGenerated(WaterTexture, new SKColor(0x2C, 0x6E, 0x9C));
            return new AcMaterial(name, WaterTexture) { Specular = 0.6f, SpecularExponent = 60 };
        }

        var texture = PickDiffuse(material);
        if (texture is null)
        {
            Warnings.Add($"material {name}: no usable diffuse texture, using flat grey");
            AddGenerated(FallbackTexture, new SKColor(0x80, 0x80, 0x80));
            return new AcMaterial(name, FallbackTexture);
        }

        var alphaTested = IsAlphaTested(material?.BaseShader);
        return new AcMaterial(name, texture)
        {
            Shader = alphaTested ? "ksPerPixelAT" : "ksPerPixel",
            AlphaTested = alphaTested,
            Ambient = 0.45f,
            Diffuse = 0.55f,
            Specular = 0.15f,
            SpecularExponent = 20,
        };
    }

    /// <summary>Embeds the first readable DDS among the diffuse-like slots and returns its KN5 texture name.</summary>
    private string? PickDiffuse(ExtractedMaterial? material)
    {
        if (material is null)
        {
            return null;
        }

        foreach (var slot in DiffuseSlots)
        {
            if (!material.Textures.TryGetValue(slot, out var path) || !path.EndsWith(".dds", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var textureName = Path.GetFileName(path);
            if (_textures.ContainsKey(textureName))
            {
                return textureName;
            }

            if (readTexture(path) is { } data)
            {
                _textures[textureName] = new AcTexture(textureName, data);
                return textureName;
            }
        }

        return null;
    }

    private void AddGenerated(string name, SKColor color)
    {
        if (_textures.ContainsKey(name))
        {
            return;
        }

        using var bitmap = new SKBitmap(4, 4);
        bitmap.Erase(color);
        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        _textures[name] = new AcTexture(name, data.ToArray());
    }

    public static string MaterialName(string materialPath) => Path.GetFileName(materialPath.Replace('\\', '/')).Split('.')[0];
}
