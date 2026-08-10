using Godot;

namespace Crest.Godot;

/// Real-window QA layout for the Renderer/ShaderMaterial input model.
public partial class CrestInputShowcase : Node3D
{
    public override void _Ready()
    {
        var args = OS.GetCmdlineUserArgs();
        if (!System.Array.Exists(args, value => value == "--input-showcase"))
            return;
        var inputCase = System.Array.Find(args, value => value.StartsWith("--input-case="));
        inputCase = inputCase?.Split('=', 2)[1] ?? "geometry";

        if (inputCase == "animated_texture")
        {
            AddInput(new CrestRegisterAnimWavesInput
            {
                Mesh = Plane(new Vector2(14, 14)),
                MaterialOverride = Material("anim_waves_add_from_tex", "_MainTex", RadialTexture(false),
                    "_Strength", 2.0f, "_HeightsOnly", 1.0f),
            });
            return;
        }
        if (inputCase == "foam_flow_textures")
        {
            AddInput(new CrestRegisterFoamInput
            {
                Mesh = Plane(new Vector2(14, 14)),
                MaterialOverride = Material("foam_add_from_tex", "_MainTex", RadialTexture(true), "_Strength", 1.0f),
            });
            AddInput(new CrestRegisterFlowInput
            {
                Mesh = Plane(new Vector2(20, 16)),
                MaterialOverride = Material("flow_add_flow_map", "_FlowMap", SolidTexture(new Color(1, 0.5f, 0, 1)),
                    "_Strength", 5.0f),
            });
            return;
        }
        if (inputCase == "clip_texture")
        {
            AddInput(new CrestRegisterClipSurfaceInput
            {
                _mode = 0,
                Mesh = Plane(new Vector2(16, 16)),
                MaterialOverride = Material("clip_surface_remove_area_texture", "_MainTex", CheckerTexture(Colors.White, Colors.Black)),
            });
            return;
        }
        if (inputCase == "clip_primitive")
        {
            AddInput(new CrestRegisterClipSurfaceInput
            {
                _primitive = 3, Scale = new Vector3(14, 5, 8), Position = new Vector3(-8, 0, 0),
            });
            AddInput(new CrestRegisterClipSurfaceInput
            {
                _primitive = 0, Scale = new Vector3(10, 10, 10), Position = new Vector3(8, 0, 0),
                _order = 1,
            });
            return;
        }
        if (inputCase == "dynamic_bump")
        {
            AddInput(new CrestRegisterDynWavesInput
            {
                Mesh = Plane(new Vector2(2, 2)),
                MaterialOverride = Material("dynamic_waves_add_bump", "_Amplitude", 0.15f, "_Radius", 8.0f),
            });
            return;
        }
        if (inputCase == "height_geometry")
        {
            AddInput(new CrestRegisterHeightInput
            {
                Mesh = Plane(new Vector2(18, 14)), Position = new Vector3(0, 2.5f, 0),
            });
            return;
        }
        if (inputCase == "albedo_texture")
        {
            AddInput(new CrestRegisterAlbedoInput
            {
                Mesh = Plane(new Vector2(16, 16)),
                MaterialOverride = Material("albedo_color", "_Texture",
                    CheckerTexture(new Color(1, 0, 0, 1), new Color(0, 1, 0, 0.2f)),
                    "_Color", Colors.White, "_Cutoff", 0.5f),
            });
            return;
        }
        if (inputCase == "shadow_geometry")
        {
            AddInput(new CrestRegisterShadowInput
            {
                Mesh = Triangle(), MaterialOverride = Material("shadow_override", "_ShadowValue", 0.0f),
                Position = new Vector3(0, 4, 0),
            });
            return;
        }

        AddInput(new CrestRegisterAnimWavesInput
        {
            Mesh = Plane(new Vector2(9, 9)),
            MaterialOverride = Material("anim_waves_set_height"),
            Position = new Vector3(-7, 2.5f, 1),
        });
        AddInput(new CrestRegisterFoamInput
        {
            Mesh = new SphereMesh { Radius = 4, Height = 8 },
            MaterialOverride = Material("foam_add_from_vert_col", "_Strength", 1.0f),
            Position = new Vector3(7, 0, 2),
        });
        AddInput(new CrestRegisterFlowInput
        {
            Mesh = Plane(new Vector2(18, 10)),
            MaterialOverride = Material("flow_fixed_direction", "_Speed", 4.0f, "_Direction", 0.25f),
            Position = new Vector3(7, 0, 2),
        });
        AddInput(new CrestRegisterAlbedoInput
        {
            Mesh = Plane(new Vector2(9, 7)),
            MaterialOverride = Material("albedo_color", "_Color", new Color(1.0f, 0.03f, 0.02f, 1.0f)),
            Position = new Vector3(6, 0, -6),
        });
        AddInput(new CrestRegisterClipSurfaceInput
        {
            _mode = 0,
            Mesh = Plane(new Vector2(7, 7)),
            MaterialOverride = Material("clip_surface_remove_area_texture"),
            Position = new Vector3(0, 0, 4),
        });
        AddInput(new CrestRegisterShadowInput
        {
            Mesh = new SphereMesh { Radius = 4, Height = 8 },
            MaterialOverride = Material("shadow_override", "_ShadowValue", 0.0f),
            Position = new Vector3(-7, 4, -7),
        });
    }

    private void AddInput(Node input)
    {
        AddChild(input);
        if (input is CrestRegisterShadowInput shadow)
            GD.Print($"Crest input showcase: shadow {shadow.GetShadowCaster()}");
        else if (input is CrestRegisterLodDataInput lodInput)
            GD.Print($"Crest input showcase: {input.GetType().Name} {lodInput.GetInjection()}");
    }
    private static PlaneMesh Plane(Vector2 size) => new() { Size = size };

    private static ArrayMesh Triangle()
    {
        var tool = new SurfaceTool();
        tool.Begin(Mesh.PrimitiveType.Triangles);
        tool.SetColor(new Color(1, 0, 0, 1)); tool.SetUV(new Vector2(0, 0)); tool.AddVertex(new Vector3(-7, 0, -6));
        tool.SetColor(new Color(0, 1, 0, 1)); tool.SetUV(new Vector2(1, 0)); tool.AddVertex(new Vector3(7, 0, -6));
        tool.SetColor(new Color(0, 0, 1, 1)); tool.SetUV(new Vector2(0.5f, 1)); tool.AddVertex(new Vector3(0, 0, 7));
        return tool.Commit();
    }

    private static Texture2D SolidTexture(Color color)
    {
        var image = Image.CreateEmpty(4, 4, false, Image.Format.Rgba8); image.Fill(color);
        return ImageTexture.CreateFromImage(image);
    }

    private static Texture2D RadialTexture(bool solidCenter)
    {
        const int size = 128;
        var image = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
        {
            var p = new Vector2((x + 0.5f) / size, (y + 0.5f) / size) * 2.0f - Vector2.One;
            var value = Mathf.Clamp(1.0f - p.Length(), 0.0f, 1.0f);
            if (!solidCenter) value *= 0.5f + 0.5f * Mathf.Cos(p.Length() * 20.0f);
            image.SetPixel(x, y, new Color(value, value, value, 1));
        }
        return ImageTexture.CreateFromImage(image);
    }

    private static Texture2D CheckerTexture(Color a, Color b)
    {
        const int size = 64;
        var image = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++) image.SetPixel(x, y, ((x / 16 + y / 16) & 1) == 0 ? a : b);
        return ImageTexture.CreateFromImage(image);
    }

    private static ShaderMaterial Material(string shaderName, params object[] parameters)
    {
        var material = new ShaderMaterial
        {
            Shader = GD.Load<Shader>($"res://addons/crest/shaders/inputs/{shaderName}.gdshader"),
        };
        for (var i = 0; i + 1 < parameters.Length; i += 2)
        {
            var value = parameters[i + 1] switch
            {
                float number => Variant.From(number),
                Color color => Variant.From(color),
                Texture2D texture => Variant.From(texture),
                Vector4 vector => Variant.From(vector),
                _ => default,
            };
            material.SetShaderParameter((string)parameters[i], value);
        }
        return material;
    }
}
