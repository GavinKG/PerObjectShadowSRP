using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

public class BlitRenderFeature : ScriptableRendererFeature {



    public BlitRenderPassSettings settings;

    BlitRenderPass blitRenderPass;


    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData) {
        renderer.EnqueuePass(blitRenderPass);
    }

    public override void Create() {
        blitRenderPass = new BlitRenderPass(settings);

    }

}
