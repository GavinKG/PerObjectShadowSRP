// Gavin_KG presents

using System.Collections.Generic;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering;
using UnityEngine;


[System.Serializable]
public class PerObjectShadowResolveSettings {

    public RenderPassEvent renderPassEvent = RenderPassEvent.BeforeRenderingTransparents;

    [Tooltip("Material applied to shadow frustums")]
    public Material resolveMat;

    [Tooltip("if true, shadows will be drawn directly to back buffer; if false, shadows will be drawn to a seperate RT named \"" + PerObjectShadowImpl.s_ScreenSpaceShadowmap + "\".")]
    public bool resolveToRenderTexture;

    // should only be shown when resolveToRenderTexture set to true
    // use R8 for grayscale shadow color (can be colored in blit pass)
    public RenderTextureFormat renderTextureFormat = RenderTextureFormat.R8;
}


public class PerObjectShadowResolvePass : ScriptableRenderPass {

    PerObjectShadowImpl Impl {
        get {
            return PerObjectShadowHelper.Instance?.Impl;
        }
    }

    RenderTargetIdentifier sssmTextureId;
    RenderTargetHandle sssmHandle; // like an int

    PerObjectShadowResolveSettings resolveSettings;

    MaterialPropertyBlock block = new MaterialPropertyBlock();

    public PerObjectShadowResolvePass(PerObjectShadowResolveSettings resolveSettings) {

        profilingSampler = new ProfilingSampler(nameof(PerObjectShadowResolvePass));

        this.resolveSettings = resolveSettings;

        this.renderPassEvent = resolveSettings.renderPassEvent;

        // get property id handle
        sssmHandle.Init(PerObjectShadowImpl.s_ScreenSpaceShadowmap);
    }

    public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor) {

        if (resolveSettings.resolveToRenderTexture) {
            cmd.GetTemporaryRT(sssmHandle.id, cameraTextureDescriptor.width, cameraTextureDescriptor.height, 0, FilterMode.Bilinear, resolveSettings.renderTextureFormat);
            sssmTextureId = new RenderTargetIdentifier(sssmHandle.id);
            // no need to execute cmd since renderer will execute it for you (and there is no profiling scope)
            // Any temporary textures that were not explicitly released will be removed after camera is done rendering.

            ConfigureTarget(sssmTextureId);
            ConfigureClear(ClearFlag.All, Color.white);
        }
    }

    public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData) {

        if (Impl == null || resolveSettings.resolveMat == null || !Impl.ShouldEvenRender) {
            return;
        }

        CommandBuffer cmd = CommandBufferPool.Get();
        using (new ProfilingScope(cmd, profilingSampler)) {

            if (resolveSettings.resolveToRenderTexture) {
                cmd.SetGlobalTexture(sssmHandle.id, sssmTextureId);
            }

            Matrix4x4[] worldToAtlasUVMatrixArray = Impl.WorldToAtlasUVMatrixArray;

            if (worldToAtlasUVMatrixArray.Length != 0) {
                Matrix4x4[] modelArray = Impl.FrustumLocalToWorldMatrixArray;
                Vector4[] sliceUVOffsetExtendArray = Impl.SliceUVOffsetExtendArray;

                
                block.SetMatrixArray(PerObjectShadowImpl.s_WorldToUVMatrix, worldToAtlasUVMatrixArray);
                block.SetVectorArray(PerObjectShadowImpl.s_SliceUVOffsetExtend, sliceUVOffsetExtendArray);

                cmd.SetGlobalVector(PerObjectShadowImpl.s_TexelSize, Impl.TexelSize);

                cmd.DrawMeshInstanced(PerObjectShadowImpl.FrustumCube, 0, resolveSettings.resolveMat, 0, modelArray, modelArray.Length, block);
            }

        }

        context.ExecuteCommandBuffer(cmd);
        CommandBufferPool.Release(cmd);
    }
}
