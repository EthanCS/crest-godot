using Godot;
using Godot.Collections;
using System.Collections.Generic;

namespace Crest.Godot;

/// C# port of Crest's patch mesh generation. Kept independent of the scene
/// backend so mesh topology can be validated before switching renderer code.
[GlobalClass]
public partial class CrestOceanBuilderCs : RefCounted
{
    public enum PatchType
    {
        Interior, Fat, FatX, FatXOuter, FatXZ, FatXZOuter,
        FatXSlimZ, SlimX, SlimXZ, SlimXFATZ, Count
    }

    public static int GetTileResolution(int lodDataResolution, int geometryDownSampleFactor) =>
        Mathf.RoundToInt(0.25f * lodDataResolution / Mathf.Max(1, geometryDownSampleFactor));

    public static ArrayMesh[] BuildPatchMeshes(int tileResolution, float extentsMultiplier)
    {
        var result = new ArrayMesh[(int)PatchType.Count];
        for (var i = 0; i < result.Length; i++)
            result[i] = BuildOceanPatch((PatchType)i, tileResolution, extentsMultiplier);
        return result;
    }

    public static Array<CrestOceanChunkRendererCs> CreateLodChunks(Node3D parent, int lodIndex,
        int lodCount, ArrayMesh[] meshes, Material material, float extentsMultiplier)
    {
        var outer = lodIndex == lodCount - 1;
        var leadSide = outer ? PatchType.FatXOuter : PatchType.SlimX;
        var trailSide = outer ? PatchType.FatXOuter : PatchType.FatX;
        var leadCorner = outer ? PatchType.FatXZOuter : PatchType.SlimXZ;
        var trailCorner = outer ? PatchType.FatXZOuter : PatchType.FatXZ;
        var tl = outer ? PatchType.FatXZOuter : PatchType.SlimXFATZ;
        var br = outer ? PatchType.FatXZOuter : PatchType.FatXSlimZ;
        var offsets = new Vector2[ lodIndex == 0 ? 16 : 12 ];
        var types = new PatchType[offsets.Length];
        if (lodIndex == 0)
        {
            offsets = new[] { new Vector2(-1.5f,1.5f),new Vector2(-.5f,1.5f),new Vector2(.5f,1.5f),new Vector2(1.5f,1.5f),
                new Vector2(-1.5f,.5f),new Vector2(-.5f,.5f),new Vector2(.5f,.5f),new Vector2(1.5f,.5f),
                new Vector2(-1.5f,-.5f),new Vector2(-.5f,-.5f),new Vector2(.5f,-.5f),new Vector2(1.5f,-.5f),
                new Vector2(-1.5f,-1.5f),new Vector2(-.5f,-1.5f),new Vector2(.5f,-1.5f),new Vector2(1.5f,-1.5f) };
            types = new[] { tl,leadSide,leadSide,leadCorner,trailSide,PatchType.Interior,PatchType.Interior,leadSide,
                trailSide,PatchType.Interior,PatchType.Interior,leadSide,trailCorner,trailSide,trailSide,br };
        }
        else
        {
            offsets = new[] { new Vector2(-1.5f,1.5f),new Vector2(-.5f,1.5f),new Vector2(.5f,1.5f),new Vector2(1.5f,1.5f),
                new Vector2(-1.5f,.5f),new Vector2(1.5f,.5f),new Vector2(-1.5f,-.5f),new Vector2(1.5f,-.5f),
                new Vector2(-1.5f,-1.5f),new Vector2(-.5f,-1.5f),new Vector2(.5f,-1.5f),new Vector2(1.5f,-1.5f) };
            types = new[] { tl,leadSide,leadSide,leadCorner,trailSide,leadSide,trailSide,leadSide,trailCorner,trailSide,trailSide,br };
        }

        var result = new Array<CrestOceanChunkRendererCs>();
        var horizontalScale = Mathf.Pow(2.0f, lodIndex);
        for (var i = 0; i < offsets.Length; i++)
        {
            var chunk = new CrestOceanChunkRendererCs { Name = $"Tile_L{lodIndex}_{(int)types[i]}" };
            parent.AddChild(chunk);
            chunk.Position = new Vector3(offsets[i].X * horizontalScale, 0.0f, offsets[i].Y * horizontalScale);
            chunk.Scale = new Vector3(horizontalScale, 1.0f, horizontalScale);
            chunk.Mesh = meshes[(int)types[i]];
            chunk.MaterialOverride = material;
            chunk.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
            chunk.SortingOffset = -lodCount + (types[i] == PatchType.Interior ? -1.0f : lodIndex);
            chunk.Setup(lodIndex);
            var rotateSide = types[i] is PatchType.FatX or PatchType.FatXOuter or PatchType.SlimX or PatchType.SlimXFATZ;
            if (rotateSide)
            {
                if (Mathf.Abs(offsets[i].Y) >= Mathf.Abs(offsets[i].X))
                    chunk.RotationDegrees = new Vector3(0.0f, 90.0f * Mathf.Sign(offsets[i].Y) * -1.0f, 0.0f);
                else if (offsets[i].X < 0.0f)
                    chunk.RotationDegrees = new Vector3(0.0f, 180.0f, 0.0f);
            }
            var rotateCorner = types[i] is PatchType.FatXZ or PatchType.SlimXZ or PatchType.FatXSlimZ or PatchType.FatXZOuter;
            if (rotateCorner)
            {
                var from = new Vector3(1.0f, 0.0f, 1.0f).Normalized();
                var to = chunk.Position.Normalized();
                if (from.Dot(to) < -0.99f)
                    chunk.RotationDegrees = new Vector3(0.0f, 180.0f, 0.0f);
                else
                    chunk.Quaternion = new Quaternion(from, to);
            }
            result.Add(chunk);
        }
        return result;
    }

    public static ArrayMesh BuildOceanPatch(PatchType patchType, int tileResolution, float extentsMultiplier)
    {
        var dx = 1.0f / tileResolution;
        var xMinus = 0.0f; var xPlus = 0.0f; var zMinus = 0.0f; var zPlus = 0.0f;
        switch (patchType)
        {
            case PatchType.Fat: xMinus = xPlus = zMinus = zPlus = 1.0f; break;
            case PatchType.FatX: case PatchType.FatXOuter: xPlus = 1.0f; break;
            case PatchType.FatXZ: case PatchType.FatXZOuter: xPlus = zPlus = 1.0f; break;
            case PatchType.FatXSlimZ: xPlus = 1.0f; zPlus = -1.0f; break;
            case PatchType.SlimX: xPlus = -1.0f; break;
            case PatchType.SlimXZ: xPlus = zPlus = -1.0f; break;
            case PatchType.SlimXFATZ: xPlus = -1.0f; zPlus = 1.0f; break;
        }

        var sideX = (int)(1.0f + tileResolution + xMinus + xPlus);
        var sideZ = (int)(1.0f + tileResolution + zMinus + zPlus);
        var startX = -0.5f - xMinus * dx; var startZ = -0.5f - zMinus * dx;
        var endX = 0.5f + xPlus * dx; var endZ = 0.5f + zPlus * dx;
        var vertices = new List<Vector3>();
        var indices = new List<int>();
        for (var j = 0; j < sideZ; j++)
        {
            var z = Mathf.Lerp(startZ, endZ, j / (float)(sideZ - 1));
            if (patchType == PatchType.FatXZOuter && j == sideZ - 1) z *= extentsMultiplier;
            for (var i = 0; i < sideX; i++)
            {
                var x = Mathf.Lerp(startX, endX, i / (float)(sideX - 1));
                if (i == sideX - 1 && (patchType == PatchType.FatXOuter || patchType == PatchType.FatXZOuter))
                    x *= extentsMultiplier;
                vertices.Add(new Vector3(x, 0.0f, z));
            }
        }
        var squaresX = sideX - 1; var squaresZ = sideZ - 1;
        for (var j = 0; j < squaresZ; j++)
            for (var i = 0; i < squaresX; i++)
            {
                var flip = (i % 2 == 1) != (j % 2 == 1);
                var i0 = i + j * sideX; var i1 = i0 + 1;
                var i2 = i0 + sideX; var i3 = i2 + 1;
                if (!flip) { indices.Add(i0); indices.Add(i1); indices.Add(i3); indices.Add(i3); indices.Add(i2); indices.Add(i0); }
                else { indices.Add(i2); indices.Add(i1); indices.Add(i3); indices.Add(i1); indices.Add(i2); indices.Add(i0); }
            }

        var arrays = new Array { };
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices.ToArray();
        arrays[(int)Mesh.ArrayType.Index] = indices.ToArray();
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        mesh.CustomAabb = mesh.GetAabb().Grow(3.0f * dx);
        mesh.ResourceName = $"CrestPatch{(int)patchType}";
        return mesh;
    }
}
