using Godot;
using Godot.Collections;

namespace Crest.Godot;

/// CPU counterpart of Unity rendering a tagged Renderer into an ocean-input
/// command buffer. It projects the actual mesh triangles into world XZ and
/// interpolates UVs, vertex colours or world height. The generated texture is
/// transient runtime data; authored data remains Mesh + ShaderMaterial.
internal static class CrestInputRasterizer
{
    internal enum ValueMode
    {
        Texture,
        VertexRed,
        Coverage,
        WorldHeight,
        Albedo,
    }

    internal static Texture2D Rasterize(MeshInstance3D renderer, int resolution,
        ValueMode mode, Texture2D? source, Color defaultSample, Vector4 textureSt,
        float cutoff = 0.0f, int surfaceFilter = -1)
    {
        var image = Image.CreateEmpty(resolution, resolution, false, Image.Format.Rgbaf);
        // Godot's Colors.Transparent is transparent white. Most simulation
        // managers consume R after format conversion, so uncovered pixels
        // must explicitly be transparent black.
        image.Fill(new Color(0, 0, 0, 0));
        var mesh = renderer.Mesh;
        if (mesh == null) return ImageTexture.CreateFromImage(image);

        WorldBounds(renderer, out var minimum, out var maximum);
        var size = maximum - minimum;
        if (size.X <= 0.00001f || size.Y <= 0.00001f)
            return ImageTexture.CreateFromImage(image);

        var sourceImage = source?.GetImage();
        var depth = new float[resolution * resolution];
        System.Array.Fill(depth, float.NegativeInfinity);

        for (var surface = 0; surface < mesh.GetSurfaceCount(); surface++)
        {
            if (surfaceFilter >= 0 && surface != surfaceFilter) continue;
            var arrays = mesh.SurfaceGetArrays(surface);
            if (arrays.Count <= (int)Mesh.ArrayType.Vertex ||
                arrays[(int)Mesh.ArrayType.Vertex].VariantType != Variant.Type.PackedVector3Array) continue;
            var vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            var uvs = arrays.Count > (int)Mesh.ArrayType.TexUV &&
                arrays[(int)Mesh.ArrayType.TexUV].VariantType == Variant.Type.PackedVector2Array
                ? arrays[(int)Mesh.ArrayType.TexUV].AsVector2Array() : System.Array.Empty<Vector2>();
            var colors = arrays.Count > (int)Mesh.ArrayType.Color &&
                arrays[(int)Mesh.ArrayType.Color].VariantType == Variant.Type.PackedColorArray
                ? arrays[(int)Mesh.ArrayType.Color].AsColorArray() : System.Array.Empty<Color>();
            var indices = arrays.Count > (int)Mesh.ArrayType.Index &&
                arrays[(int)Mesh.ArrayType.Index].VariantType == Variant.Type.PackedInt32Array
                ? arrays[(int)Mesh.ArrayType.Index].AsInt32Array() : System.Array.Empty<int>();
            var triangleIndexCount = indices.Length > 0 ? indices.Length : vertices.Length;
            for (var index = 0; index + 2 < triangleIndexCount; index += 3)
            {
                var ia = indices.Length > 0 ? indices[index] : index;
                var ib = indices.Length > 0 ? indices[index + 1] : index + 1;
                var ic = indices.Length > 0 ? indices[index + 2] : index + 2;
                if (ia < 0 || ib < 0 || ic < 0 || ia >= vertices.Length || ib >= vertices.Length || ic >= vertices.Length)
                    continue;
                var a = renderer.GlobalTransform * vertices[ia];
                var b = renderer.GlobalTransform * vertices[ib];
                var c = renderer.GlobalTransform * vertices[ic];
                var pa = Pixel(a, minimum, size, resolution);
                var pb = Pixel(b, minimum, size, resolution);
                var pc = Pixel(c, minimum, size, resolution);
                var area = Edge(pa, pb, pc);
                if (Mathf.Abs(area) < 0.00001f) continue;

                var minX = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(pa.X, Mathf.Min(pb.X, pc.X))), 0, resolution - 1);
                var maxX = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(pa.X, Mathf.Max(pb.X, pc.X))), 0, resolution - 1);
                var minY = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(pa.Y, Mathf.Min(pb.Y, pc.Y))), 0, resolution - 1);
                var maxY = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(pa.Y, Mathf.Max(pb.Y, pc.Y))), 0, resolution - 1);
                var uva = ia < uvs.Length ? uvs[ia] : Vector2.Zero;
                var uvb = ib < uvs.Length ? uvs[ib] : Vector2.Zero;
                var uvc = ic < uvs.Length ? uvs[ic] : Vector2.Zero;
                var ca = ia < colors.Length ? colors[ia] : Colors.White;
                var cb = ib < colors.Length ? colors[ib] : Colors.White;
                var cc = ic < colors.Length ? colors[ic] : Colors.White;

                for (var y = minY; y <= maxY; y++)
                for (var x = minX; x <= maxX; x++)
                {
                    var p = new Vector2(x + 0.5f, y + 0.5f);
                    var wa = Edge(pb, pc, p) / area;
                    var wb = Edge(pc, pa, p) / area;
                    var wc = 1.0f - wa - wb;
                    if (wa < -0.0001f || wb < -0.0001f || wc < -0.0001f) continue;
                    var worldY = wa * a.Y + wb * b.Y + wc * c.Y;
                    var pixelIndex = y * resolution + x;
                    if (worldY < depth[pixelIndex]) continue;
                    depth[pixelIndex] = worldY;
                    var uv = wa * uva + wb * uvb + wc * uvc;
                    uv = new Vector2(uv.X * textureSt.X + textureSt.Z, uv.Y * textureSt.Y + textureSt.W);
                    var vertexColor = wa * ca + wb * cb + wc * cc;
                    var sampled = Sample(sourceImage, uv, defaultSample);
                    var value = mode switch
                    {
                        ValueMode.Texture => sampled,
                        ValueMode.VertexRed => new Color(vertexColor.R, vertexColor.R, vertexColor.R, vertexColor.R),
                        ValueMode.Coverage => Colors.White,
                        ValueMode.WorldHeight => new Color(worldY, 0.0f, 0.0f, 1.0f),
                        ValueMode.Albedo => sampled * vertexColor,
                        _ => Colors.Transparent,
                    };
                    if (mode == ValueMode.Albedo && value.A < cutoff) value = new Color(0, 0, 0, 0);
                    image.SetPixel(x, y, value);
                }
            }
        }
        return ImageTexture.CreateFromImage(image);
    }

    internal static void WorldBounds(MeshInstance3D renderer, out Vector2 minimum, out Vector2 maximum)
    {
        var bounds = renderer.Mesh?.GetAabb() ?? new Aabb(Vector3.Zero, Vector3.Zero);
        minimum = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        maximum = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
        for (var i = 0; i < 8; i++)
        {
            var local = bounds.Position + new Vector3((i & 1) == 0 ? 0 : bounds.Size.X,
                (i & 2) == 0 ? 0 : bounds.Size.Y, (i & 4) == 0 ? 0 : bounds.Size.Z);
            var world = renderer.GlobalTransform * local;
            minimum = new Vector2(Mathf.Min(minimum.X, world.X), Mathf.Min(minimum.Y, world.Z));
            maximum = new Vector2(Mathf.Max(maximum.X, world.X), Mathf.Max(maximum.Y, world.Z));
        }
        if (!float.IsFinite(minimum.X)) minimum = maximum = new Vector2(renderer.GlobalPosition.X, renderer.GlobalPosition.Z);
    }

    private static Vector2 Pixel(Vector3 world, Vector2 minimum, Vector2 size, int resolution) =>
        new((world.X - minimum.X) / size.X * resolution, (world.Z - minimum.Y) / size.Y * resolution);
    private static float Edge(Vector2 a, Vector2 b, Vector2 p) =>
        (p.X - a.X) * (b.Y - a.Y) - (p.Y - a.Y) * (b.X - a.X);

    private static Color Sample(Image? image, Vector2 uv, Color fallback)
    {
        if (image == null || image.IsEmpty()) return fallback;
        uv = new Vector2(Mathf.PosMod(uv.X, 1.0f), Mathf.PosMod(uv.Y, 1.0f));
        var fx = uv.X * image.GetWidth() - 0.5f;
        var fy = uv.Y * image.GetHeight() - 0.5f;
        var x0 = Mathf.FloorToInt(fx); var y0 = Mathf.FloorToInt(fy);
        var tx = fx - x0; var ty = fy - y0;
        int Wrap(int value, int extent) => Mathf.PosMod(value, extent);
        var c00 = image.GetPixel(Wrap(x0, image.GetWidth()), Wrap(y0, image.GetHeight()));
        var c10 = image.GetPixel(Wrap(x0 + 1, image.GetWidth()), Wrap(y0, image.GetHeight()));
        var c01 = image.GetPixel(Wrap(x0, image.GetWidth()), Wrap(y0 + 1, image.GetHeight()));
        var c11 = image.GetPixel(Wrap(x0 + 1, image.GetWidth()), Wrap(y0 + 1, image.GetHeight()));
        return c00.Lerp(c10, tx).Lerp(c01.Lerp(c11, tx), ty);
    }
}
