// Gavin_KG presents

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering;


public class PerObjectShadowRendererFeature : ScriptableRendererFeature {

    public RenderPassEvent renderPassEvent = RenderPassEvent.AfterRenderingShadows;

    public PerObjectShadowSettings perObjectShadowSettings = new PerObjectShadowSettings();

    PerObjectShadowPass perObjectShadowPass;


    // exec per frame
    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData) {
        renderer.EnqueuePass(perObjectShadowPass);
    }

    // exec when added to pipeline
    public override void Create() {

        perObjectShadowPass = new PerObjectShadowPass(perObjectShadowSettings, renderPassEvent);

    }


}
