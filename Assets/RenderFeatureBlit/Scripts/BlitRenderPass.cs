// Gavin_KG presents

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering;

[System.Serializable]
public class BlitRenderPassSettings {

    public enum SourceTextureFrom {
        RenderTextureObject,
        GlobalTexture
    }

    public RenderPassEvent renderPassEvent = RenderPassEvent.BeforeRenderingTransparents;

    public Material blitMat = null;

    public SourceTextureFrom sourceTextureFrom = SourceTextureFrom.GlobalTexture;

    // SourceTextureFrom options:
    public RenderTexture renderTextureObject;
    public string globalTextureName;

    
    
}

public class BlitRenderPass : ScriptableRenderPass {

    BlitRenderPassSettings settings;

    public BlitRenderPass(BlitRenderPassSettings settings) {

        profilingSampler = new ProfilingSampler(nameof(BlitRenderPass));

        this.settings = settings;
        this.renderPassEvent = settings.renderPassEvent;
    }

    public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData) {
        CommandBuffer cmd = CommandBufferPool.Get();
        using (new ProfilingScope(cmd, profilingSampler)) {

            if (settings.sourceTextureFrom == BlitRenderPassSettings.SourceTextureFrom.RenderTextureObject) {
                if (settings.renderTextureObject != null) {
                    if (settings.blitMat == null) {
                        cmd.Blit(settings.renderTextureObject, BuiltinRenderTextureType.CurrentActive);
                    } else {
                        cmd.Blit(settings.renderTextureObject, BuiltinRenderTextureType.CurrentActive, settings.blitMat);
                    }
                }
            } else if (settings.sourceTextureFrom == BlitRenderPassSettings.SourceTextureFrom.GlobalTexture) {
                if (settings.globalTextureName.Length != 0) {
                    RenderTargetIdentifier id = new RenderTargetIdentifier(settings.globalTextureName);
                    if (settings.blitMat == null) {
                        cmd.Blit(id, BuiltinRenderTextureType.CurrentActive);
                    } else {
                        cmd.Blit(id, BuiltinRenderTextureType.CurrentActive, settings.blitMat);
                    }
                }
            }
        }

        context.ExecuteCommandBuffer(cmd);
        CommandBufferPool.Release(cmd);
    }
}
