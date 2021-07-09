// Gavin_KG presents

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering;



public class PerObjectShadowResolveRendererFeature : ScriptableRendererFeature {


    public PerObjectShadowResolveSettings perObjectShadowResolveSettings = new PerObjectShadowResolveSettings();

    PerObjectShadowResolvePass perObjectShadowResolvePass;

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData) {
        renderer.EnqueuePass(perObjectShadowResolvePass);
    }

    public override void Create() {
        perObjectShadowResolvePass = new PerObjectShadowResolvePass(perObjectShadowResolveSettings);

    }
}
