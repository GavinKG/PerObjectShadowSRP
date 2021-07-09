// Gavin_KG presents

using System.Collections.Generic;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering;
using UnityEngine;

public class PerObjectShadowPass : ScriptableRenderPass {

    // RT lifecycle are taken care of by this pass, not resolve pass.
    RenderTargetIdentifier atlasTextureId;
    RenderTargetHandle atlasHandle;

    // settings adequate for drawing atlas.
    PerObjectShadowSettings settings;

    PerObjectShadowImpl impl;



    public PerObjectShadowPass(PerObjectShadowSettings settings, RenderPassEvent renderPassEvent) {

        profilingSampler = new ProfilingSampler(nameof(PerObjectShadowPass));

        this.settings = settings;

        this.renderPassEvent = renderPassEvent;

        // init impl class
        impl = new PerObjectShadowImpl(settings);

        if (settings.updateMethod == PerObjectShadowSettings.UpdateMethod.OnInit) {
            RegisterObjects();
        }

        // get property id handle
        atlasHandle.Init(PerObjectShadowImpl.s_PerObjectShadowAtlas);
    }

    ~PerObjectShadowPass() {
        //todo: release rt
    }

    // before render pass, per frame
    public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor) {

        // try register impl to helper, per frame to avoid getting null in resolve pass.
        if (PerObjectShadowHelper.Instance != null) {
            PerObjectShadowHelper.Instance.Impl = impl;
        }

        if (settings.maxObjects == 0) {
            return;
        }

        impl.SetupAtlasProperty();

        Vector2Int rtRes = impl.AtlasResolution;

        // create render texture

        cmd.GetTemporaryRT(atlasHandle.id, rtRes.x, rtRes.y, 16, FilterMode.Bilinear, RenderTextureFormat.Shadowmap);
        atlasTextureId = new RenderTargetIdentifier(atlasHandle.id);
        // Any temporary textures that were not explicitly released will be removed after camera is done rendering

        // setup attachments
        ConfigureTarget(atlasTextureId); // unity treats shadowmap as color attachment at beginning
        ConfigureClear(ClearFlag.All, Color.black);

        // update objects
        if (settings.updateMethod == PerObjectShadowSettings.UpdateMethod.PerFrame) {
            RegisterObjects();
        }

    }

    // inside render pass, per frame
    // currently does nothing
    // public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData) { }

    public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData) {

        if (!impl.ShouldEvenRender) {
            return;
        }

        impl.SetupRenderingData(renderingData.cameraData.camera);

        CommandBuffer cmd = CommandBufferPool.Get();
        using (new ProfilingScope(cmd, profilingSampler)) {

            cmd.SetGlobalTexture(atlasHandle.id, atlasTextureId); // for resolve pass

            foreach (PerObjectShadowImpl.SliceData sliceData in impl.SliceDataList) {
                if (!sliceData.ShouldRender) {
                    continue;
                }
                cmd.SetViewport(new Rect(sliceData.sliceDataPerFrame.sliceOffset.x, sliceData.sliceDataPerFrame.sliceOffset.y, sliceData.sliceDataPerFrame.sliceResolution.x, sliceData.sliceDataPerFrame.sliceResolution.y));
                cmd.SetViewProjectionMatrices(sliceData.sliceDataPerFrame.shadowViewMatrix, sliceData.sliceDataPerFrame.shadowProjMatrix);
                Vector3 lightDir = settings.LightDirection;
                context.ExecuteCommandBuffer(cmd); // cmd barrier
                cmd.Clear();
                foreach (Renderer r in sliceData.renderers) {

                    int submeshCount;
                    switch (r) {
                        case MeshRenderer meshRenderer:
                            submeshCount = meshRenderer.GetComponent<MeshFilter>().sharedMesh.subMeshCount;
                            break;
                        case SkinnedMeshRenderer skinnedMeshRenderer:
                            submeshCount = skinnedMeshRenderer.sharedMesh.subMeshCount;
                            break;
                        default:
                            submeshCount = 0;
                            break;
                    }

                    for (int i = 0; i < submeshCount; ++i) {
                        cmd.DrawRenderer(r, r.sharedMaterial, i, settings.usePass);
                    }
                }
            }

            // set things back to camera view
            ref CameraData cameraData = ref renderingData.cameraData;
            Camera camera = cameraData.camera;
            cmd.SetViewProjectionMatrices(camera.worldToCameraMatrix, camera.projectionMatrix);
        }

        context.ExecuteCommandBuffer(cmd);
        CommandBufferPool.Release(cmd);
    }

    void RegisterObjects() {
        switch (settings.objectToDraw) {
            case PerObjectShadowSettings.ObjectToDraw.ByTag:
                impl.RegisterGameObjectsByTag(settings.tag);
                break;
            default:
                break;
        }
    }
}
