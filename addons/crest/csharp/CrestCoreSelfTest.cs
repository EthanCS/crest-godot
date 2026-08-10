using Godot;

namespace Crest.Godot;

/// Headless-friendly regression checks for the migrated CPU cascade logic.
public partial class CrestCoreSelfTest : Node
{
    public override void _Ready()
    {
        var transform = new CrestLodTransformCs(3, 256);
        transform.UpdateTransforms(8.0f, new Vector2(13.25f, -7.5f));
        Check(Mathf.IsEqualApprox(transform.TexelWidth[0], 0.125f), "LOD0 texel width");
        Check(Mathf.IsEqualApprox(transform.TexelWidth[1], 0.25f), "LOD1 texel width");
        Check(Mathf.IsEqualApprox(transform.CascadeWorldSize(2, 8.0f), 128.0f), "cascade size");
        Check(Mathf.IsEqualApprox(transform.CascadeDataCurrent[6], 1.0f), "active cascade weight");
        Check(Mathf.IsEqualApprox(transform.CascadeDataCurrent[6 + 3 * 8], 0.0f), "duplicate cascade weight");
        Check(transform.SuggestDataLod(new Vector2(13.0f, -7.5f)) == 0, "LOD suggestion");
        Check(CrestOceanBuilderCs.GetTileResolution(256, 2) == 32, "tile resolution");
        var patch = CrestOceanBuilderCs.BuildOceanPatch(CrestOceanBuilderCs.PatchType.Interior, 32, 100.0f);
        Check(patch.GetSurfaceCount() == 1, "patch surface count");
        Check(patch.SurfaceGetArrayLen(0) == 1089, "patch vertex count");
        Check(patch.SurfaceGetArrayIndexLen(0) == 6144, "patch index count");
        var manager = new CrestLodDataMgrCs();
        manager.InitSim(8, 2, RenderingDevice.DataFormat.R16G16B16A16Sfloat, true);
        if (manager.Device != null)
        {
            Check(manager.CurrentTexture().IsValid, "RD current texture");
            Check(manager.TargetTexture().IsValid, "RD target texture");
            manager.SwapTargets();
            Check(manager.TextureArray.TextureRdRid.IsValid, "RD texture bridge");
            manager.FreeRids();
            for (var i = 0; i < 5; i++)
                CrestRDComputeCs.FlushDeferredFrees();
        }
        var waveSettings = new CrestSimSettingsWave();
        Check(Mathf.IsEqualApprox(waveSettings.courant_number, 0.7f), "wave settings defaults");
        var foamSettings = new CrestSimSettingsFoam();
        Check(foamSettings.prewarm && Mathf.IsEqualApprox(foamSettings.foam_fade_rate, 0.8f), "foam settings defaults");
        var waterBody = new CrestWaterBodyCs();
        Check(waterBody.ContainsXz(new Vector3(0, 0, 0)), "water body bounds");
        waterBody.Free();
        var simpleFloating = new CrestSimpleFloatingObject();
        var boatProbes = new CrestBoatProbes();
        Check(simpleFloating is CrestFloatingObjectBase, "simple floating object base contract");
        Check(boatProbes is CrestFloatingObjectBase, "boat probes base contract");
        simpleFloating.Free(); boatProbes.Free();
        var shape = new CrestShapeGerstner
        {
            wave_data = new[] { 1.0f, 0.0f, 1.0f, 0.0f, 1.0f, 0.0f, 0.0f, 1.0f },
            weight = 0.5f,
        };
        AddChild(shape);
        Check(Mathf.IsEqualApprox(CrestCollisionCs.SampleDisplacement(Vector2.Zero, 0.0).Y, 1.5f), "Gerstner displacement");
        Check(CrestCollisionCs.SampleHeightAndVelocity(Vector2.Zero, 0.2, 0.016, 0.0f, out _, out _), "collision velocity query");
        shape.Free();
        var spectrum = new CrestWaveSpectrum { multiplier = 0.03f };
        Check(spectrum.get_amplitude(8.0f, 8) > 0.0f, "spectrum amplitude");
        var spectrumRng = new RandomNumberGenerator { Seed = 7 };
        Check(spectrum.generate_wave_data(2, spectrumRng).Count == CrestWaveSpectrum.NumOctaves * 2, "spectrum sampling");
        var gerstnerTransform = new CrestLodTransformCs(3, 64);
        gerstnerTransform.UpdateTransforms(8.0f, Vector2.Zero);
        var fullWeight = new CrestShapeGerstner { spectrum = spectrum, random_seed = 17, weight = 1.0f };
        fullWeight.Regenerate(8.0f, gerstnerTransform);
        var fullDisplacement = fullWeight.ComputeDisplacement(new Vector2(2.0f, -3.0f), 0.7);
        Check(fullDisplacement.Length() > 0.0001f, "Gerstner generated displacement");
        var oldVersion = fullWeight.Version;
        spectrum.EmitChanged();
        Check(fullWeight.IsDirty, "Gerstner spectrum change dirty flag");
        fullWeight.Regenerate(8.0f, gerstnerTransform);
        Check(fullWeight.Version == oldVersion + 1, "Gerstner spectrum change regeneration");
        Check(fullWeight.ComputeDisplacement(Vector2.Zero, 0.0, 0).Length() <=
            fullWeight.ComputeDisplacement(Vector2.Zero, 0.0).Length() + 10.0f, "Gerstner max LOD query");
        fullWeight.Free();
        if (RenderingServer.GetRenderingDevice() != null)
        {
            var fft = new CrestFFTComputeCs();
            Check(fft.Initialize(16), "FFT pipeline initialization");
            fft.RebuildSpectrum(spectrum, Vector2.Right, 0.145f, 7.0f);
            fft.AdvanceTime(0.1f, 1.0f, spectrum.chop);
            fft.FreeRids();
            for (var i = 0; i < 5; i++)
                CrestRDComputeCs.FlushDeferredFrees();
        }
        var foamInput = new CrestRegisterFoamInputCs
        {
            rect_size = new Vector2(20.0f, 8.0f),
            strength = 2.5f,
            sphere_mode = true,
        };
        AddChild(foamInput);
        var injection = foamInput.GetInjection();
        Check((float)injection["strength"] == 2.5f, "foam input strength");
        Check((float)injection["mode"] == 1.0f, "foam input mode");
        Check(foamInput.IsInGroup("crest_foam_input"), "foam input group");
        foamInput.Free();
        var flowInput = new CrestRegisterFlowInputCs
        {
            fixed_direction = true,
            speed = 3.0f,
            direction_degrees = 90.0f,
        };
        AddChild(flowInput);
        var flowInjection = flowInput.GetInjection();
        var fixedVelocity = (Vector2)flowInjection["fixed_velocity"];
        Check(Mathf.IsEqualApprox(fixedVelocity.X, 0.0f) && Mathf.IsEqualApprox(fixedVelocity.Y, 3.0f), "flow direction");
        Check(flowInput.IsInGroup("crest_flow_input"), "flow input group");
        flowInput.Free();
        var depthInput = new CrestRegisterSeaFloorDepthInputCs();
        AddChild(depthInput);
        Check(depthInput.IsInGroup("crest_depth_input"), "depth input group");
        depthInput.Free();
        var clipInput = new CrestRegisterClipSurfaceInputCs();
        AddChild(clipInput);
        Check((float)clipInput.GetInjection()["mode"] == 0.0f, "clip input mode");
        clipInput.Free();
        var albedoInput = new CrestRegisterAlbedoInputCs();
        AddChild(albedoInput);
        Check(albedoInput.IsInGroup("crest_albedo_input"), "albedo input group");
        albedoInput.Free();
        var shadowInput = new CrestRegisterShadowInputCs();
        AddChild(shadowInput);
        Check(shadowInput.IsInGroup("crest_shadow_input"), "shadow input group");
        shadowInput.Free();
        var animInput = new CrestRegisterAnimWavesInputCs();
        AddChild(animInput);
        Check(animInput.IsInGroup("crest_anim_waves_input"), "animated waves input group");
        animInput.Free();
        var foamManager = new CrestFoamSimulationManagerCs();
        foamManager.InitManager(8, 2);
        if (foamManager.Data.Device != null)
            Check(foamManager.Data.CurrentTexture().IsValid, "foam manager texture");
        Check(foamManager.UpdateSchedule(1.0f / 60.0f) == 1, "foam manager substep");
        Check(foamManager.DispatchInputs(new Rid(), new global::Godot.Collections.Array<global::Godot.Collections.Dictionary>(), 0.0f) == 0, "foam input dispatch empty");
        foamManager.NotifyTeleport();
        Check(foamManager.NeedsPrewarm, "foam manager prewarm");
        foamManager.FreeRids();
        for (var i = 0; i < 5; i++)
            CrestRDComputeCs.FlushDeferredFrees();
        var depthManager = new CrestSeaFloorDepthManagerCs();
        depthManager.init_mgr(8, 2);
        if (depthManager.Data.Device != null)
            Check(depthManager.Data.CurrentTexture().IsValid, "depth manager texture");
        depthManager.free_rids();
        for (var i = 0; i < 5; i++)
            CrestRDComputeCs.FlushDeferredFrees();
        var flowManager = new CrestFlowManagerCs();
        flowManager.init_mgr(8, 2);
        if (flowManager.Data.Device != null)
            Check(flowManager.Data.CurrentTexture().IsValid, "flow manager texture");
        flowManager.free_rids();
        for (var i = 0; i < 5; i++)
            CrestRDComputeCs.FlushDeferredFrees();
        var clipManager = new CrestClipSurfaceManagerCs();
        clipManager.init_mgr(8, 2);
        if (clipManager.Data.Device != null)
            Check(clipManager.Data.CurrentTexture().IsValid, "clip manager texture");
        clipManager.free_rids();
        for (var i = 0; i < 5; i++)
            CrestRDComputeCs.FlushDeferredFrees();
        var albedoManager = new CrestAlbedoManagerCs();
        albedoManager.init_mgr(8, 2);
        if (albedoManager.Data.Device != null)
            Check(albedoManager.Data.CurrentTexture().IsValid, "albedo manager texture");
        albedoManager.free_rids();
        for (var i = 0; i < 5; i++)
            CrestRDComputeCs.FlushDeferredFrees();
        var shadowManager = new CrestShadowManagerCs();
        shadowManager.init_mgr(8, 2);
        if (shadowManager.Data.Device != null)
            Check(shadowManager.Data.CurrentTexture().IsValid, "shadow manager texture");
        shadowManager.free_rids();
        for (var i = 0; i < 5; i++)
            CrestRDComputeCs.FlushDeferredFrees();
        var animatedManager = new CrestAnimatedWavesManagerCs();
        animatedManager.init_mgr(8, 2);
        if (animatedManager.Data.Device != null)
            Check(animatedManager.wave_buffer.IsValid, "animated waves buffer");
        animatedManager.free_rids();
        for (var i = 0; i < 5; i++)
            CrestRDComputeCs.FlushDeferredFrees();
        var dynamicManager = new CrestDynamicWavesManagerCs();
        dynamicManager.init_mgr(8, 2);
        if (dynamicManager.Data.Device != null)
            Check(dynamicManager.Data.TargetTexture().IsValid, "dynamic waves buffers");
        dynamicManager.free_rids();
        for (var i = 0; i < 5; i++)
            CrestRDComputeCs.FlushDeferredFrees();
        var floatingViewer = new Node3D { Position = new Vector3(5000.0f, 0.0f, 20.0f) };
        var floatingMarker = new Node3D { Position = new Vector3(100.0f, 0.0f, 0.0f) };
        var floatingOrigin = new CrestFloatingOrigin { threshold = 4096.0f, viewpoint = floatingViewer };
        AddChild(floatingViewer); AddChild(floatingMarker); AddChild(floatingOrigin);
        floatingOrigin._PhysicsProcess(0.0);
        Check(Mathf.IsEqualApprox(floatingViewer.GlobalPosition.X, 0.0f), "floating viewer shift");
        Check(Mathf.IsEqualApprox(floatingMarker.GlobalPosition.X, -4900.0f), "floating scene shift");
        floatingViewer.Free(); floatingMarker.Free(); floatingOrigin.Free();
        GD.Print("Crest C# core self-test passed");
        GetTree().Quit();
    }

    private static void Check(bool condition, string name)
    {
        if (!condition)
            throw new System.InvalidOperationException($"Crest C# core self-test failed: {name}");
    }
}
