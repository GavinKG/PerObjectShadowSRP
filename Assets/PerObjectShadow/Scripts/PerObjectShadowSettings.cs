// Gavin_KG presents

using UnityEngine;

/// <summary>
/// Can be changed per frame.
/// </summary>
[System.Serializable]
public class PerObjectShadowSettings {
    public enum SliceResolution {
        _16 = 16,
        _32 = 32,
        _64 = 64,
        _128 = 128,
        _256 = 256,
        _512 = 512,
        _1024 = 1024,
        _2048 = 2048,
        _4096 = 4096
    }

    public enum Filtering {
        None,
        PCF2x2,
        PCF3x3,

    }

    public enum ObjectToDraw {
        Manually,
        ByTag,
    }

    public enum UpdateMethod {
        Manually,
        OnInit,
        PerFrame
    }

    public enum LightDirectionFrom {
        MainDirectionalLight,
        EulerAngles
    }

    #region Unity Inspector

    [Range(0, 16)]
    public int maxObjects = 4;

    public SliceResolution sliceMaxResolution = SliceResolution._512;

    public SliceResolution sliceMinResolution = SliceResolution._128;

    // not used right now
    // public Filtering filtering = Filtering.PCF3x3;

    public ObjectToDraw objectToDraw = ObjectToDraw.ByTag;

    // ObjectToDraw options:
    public string tag = "PerObjectShadow";
    public string layer;

    public UpdateMethod updateMethod = UpdateMethod.PerFrame;

    public LightDirectionFrom lightDirectionFrom = LightDirectionFrom.EulerAngles;

    // LightDirectionFrom options:
    public Vector3 eulerAngles = new Vector3(50, -30, 0);

    public float frustumExtend = 3f; // smaller value to prevent penetrating

    // which pass in the shader should be used for depth
    // currently in Lit.shader:
    //   ForwardLit
    //   ShadowCaster = 1
    //   GBuffer = 2
    //   DepthOnly = 3
    //   DepthNormals = 4
    //   Meta = 5
    public int usePass = 3; // should use 3 since ShadowCaster pass might have shadow (normal) bias baked in (true for Lit.shader).

    /*
    [Tooltip("Will use Unity's job system to calculate per-slice data in parallel. Default is on to improve performance.")]
    public bool useJobs = true;
    */


    [Header("Debug")]
    [Range(0.1f, 1.0f)]
    public float globalResScaleFactor = 1.0f;
    public bool frustumCulling = true;
    public bool adaptiveRes = true;
    [Range(0, 0.1f)] public float padding = 0.01f; // prevent from sampling outside atlas (due to tex/pcf filtering) .in unity unit (meter), not in texel!
    [Range(0, 4)] public int adaptiveResRemapFactor = 3;

    #endregion

    public Quaternion LightRotation {
        get {
            switch (lightDirectionFrom) {
                case LightDirectionFrom.EulerAngles:
                    return Quaternion.Euler(eulerAngles);
                default:
                    return Quaternion.identity;
            }
        }
    }

    public Vector3 LightDirection {
        get {
            return LightRotation * Vector3.up;
        }
    }

}

