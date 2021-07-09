// Gavin_KG presents

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering;
using System.Text;

/// <summary>
/// A singleton MonoBehaviour class used to connect shadow (generating) pass and shadow resolve pass.
/// Since we are not modifying URP, two seperate passes cannot communicate with each other.
/// If using custom SRP, this class can be implemented directly into the scriptable renderer.
/// Only PerObjectShadowImpl class are shared between two passes.
/// </summary>
public class PerObjectShadowHelper : MonoBehaviour {

    [Header("Debug")]
    public bool drawBounds = false;
    public bool drawFrustumMesh = false;
    public bool onscreenStatistics = false;


    public static PerObjectShadowHelper Instance {
        get {
            if (instance == null) {
                instance = FindObjectOfType<PerObjectShadowHelper>();
            }
            return instance;
        }
    }
    static PerObjectShadowHelper instance;

    // shared data
    public PerObjectShadowImpl Impl { get; set; }


    void Awake() {
        DontDestroyOnLoad(gameObject);
    }

    private void OnGUI() {
        if (!onscreenStatistics || Impl == null) {
            return;
        }

        GUILayout.Label("-- Per Object Shadow ---");
        GUILayout.Label("AtlasRes: " + Impl.AtlasResolution.ToString());
        GUILayout.Label("ObjectCount: " + Impl.ValidSliceCount.ToString());
        foreach (PerObjectShadowImpl.SliceData data in Impl.SliceDataList) {
            if (!data.ShouldRender) {
                continue;
            }
            StringBuilder sb = new StringBuilder();
            sb.Append(" - ");
            sb.Append(data.gameObject.name);
            sb.Append(", res: ");
            sb.Append(data.sliceDataPerFrame.sliceResolution.ToString());

            GUILayout.Label(sb.ToString());
        }
        GUILayout.Label("");
    }

    private void OnDrawGizmos() {

        if (drawBounds) {
            Impl?.DrawBoundsGizmos();
        }

        if (drawFrustumMesh) {
            Impl?.DrawFrustumCubeGizmos();
        }
    }

}
