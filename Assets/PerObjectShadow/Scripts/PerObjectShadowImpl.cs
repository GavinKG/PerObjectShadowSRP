// Gavin_KG presents

using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using Unity.Jobs;
using UnityEngine.Profiling;
using Unity.Burst;
// using UnityEngine.Rendering.Universal; // this file should be RP-neutral


/// <summary>
/// 
/// If used inside a URP-like custom SRP, this class can be directly implemented inside your ScriptableRenderer subclass.
/// 
/// terms:
/// - World Space (WS): Same as usual
///        | "View Matrix"
/// - Shadow View Space: centered at near plane's center (bounds' back face), oriented as the light.
///        | "Projection Matrix"
/// - Shadow Clip Space: clip space for a single shadow caster, just like a camera (directional->orthogonal, spot->perspective)
/// 
/// </summary>
public partial class PerObjectShadowImpl {

    // more or less like ShadowSliceData in URP, shared between shadow pass and resolve pass.
    public class SliceData {

        // update when RegisterGameObject (assume not changed during gameplay)
        public GameObject gameObject = null; // tracked object, can be null to indicate a invalid slice
        public Renderer[] renderers; // tracked renders inside this object

        // Updated per frame:
        // all variables marked with [TEMP] are not used in render passes. They might be removed when debugging is not necessary.
        // all variables marked with [TEMP][DEBUG] are used to draw debug gizmos.

        public struct SliceDataPerFrame {
            // UpdateSliceData_RetrieveSceneData()
            public Bounds boundsWS; // bounds in world space

            // UpdateSliceData_BeforeCulling()
            // public Matrix4x4 worldToShadowViewMatrixForBounds; // [TEMP] differences: use bounds' center instead of near plane center
            public Matrix4x4 shadowViewToWorldMatrixForBounds; // [TEMP][DEBUG]
            public Matrix4x4 shadowViewMatrix;
            public Matrix4x4 shadowProjMatrix; // OpenGL-like proj matrix, following unity's convension. Do not pass directly to shader. Use Unity API instead!!
            public Vector2Int sliceResolution; // slice resolution, might not be the same as settings.perObjectShadowResolution due to adaptive size turned on
            public Bounds boundsSVS; // [TEMP][DEBUG] bounds in shadow view space, encapsulating boundsWS
            public Vector3 nearPlaneCenterWS; // [TEMP][DEBUG]
            public Matrix4x4 frustumLocalToWorldMatrix; // used directly by shader (in vertex)

            // UpdateSliceData_Culling()
            public bool disabled; // true: obj might be culled, no need to draw shadow/frustum.

            // UpdateSliceData_AtlasPacking()
            public Vector2Int sliceOffset; // in pixel, / ShadowTextureResolution to get uv offset

            // UpdateSliceData_PrepareResolveData()
            public Matrix4x4 worldToAtlasUVMatrix; // used directly by shader (in fragment). UV are not flipped based on platform (unity conversion).
            public Vector4 sliceUVOffsetExtend; // offset + extend(size of uv range). Shader use this range to clamp uv, prevent sampling in other slices. xy: minUV, zw: extend
        }

        public SliceDataPerFrame sliceDataPerFrame;

        public bool IsValid { get { return gameObject != null && gameObject.activeInHierarchy; } }

        public bool ShouldRender { get { return IsValid && !sliceDataPerFrame.disabled; } }

        /// <summary>
        /// or, "Validate"
        /// </summary>
        /// <param name="gameObject"></param>
        public void SetGameObject(GameObject gameObject) {
            this.gameObject = gameObject;
            this.renderers = gameObject.GetComponentsInChildren<Renderer>();
        }

        public void Invalidate() {
            gameObject = null;
        }
    }


    #region Properties 

    // Settings exposed to Unity inspector. Read only
    public PerObjectShadowSettings Settings { get; private set; }

    /// <summary>
    /// All registered gameobject will have its slice data store in here.
    /// Length = maxObjects, will NOT contain null.
    /// This is NOT the final list used in render pass, since gameobject can be destroyed or culled.
    /// </summary>
    public List<SliceData> SliceDataList { get; private set; }

    /// <summary>
    /// Shadow atlas texture resolution.
    /// </summary>
    public Vector2Int AtlasResolution { get; private set; }

    #endregion

    #region Getters
    // All getters should be used AFTER UpdateData is called to get the up-to-date result.

    public Vector2 TexelSize {
        get {
            return Vector2.one / AtlasResolution;
        }
    }

    /// <summary>
    /// valid: object exists + object are not being culled (if frustum culling is enabled).
    /// </summary>
    public int ValidSliceCount {
        get {
            int c = 0;
            foreach (SliceData data in SliceDataList) {
                if (!data.sliceDataPerFrame.disabled) {
                    ++c;
                }
            }
            return c;
        }
    }

    /// <summary>
    /// Commonly used in MaterialPropertyBlock.SetMatrixArray
    /// </summary>
    public Matrix4x4[] WorldToAtlasUVMatrixArray {
        get {
            int validCount = ValidSliceCount;
            if (worldToAtlasUVMatrixArray == null || worldToAtlasUVMatrixArray.Length != validCount) {
                worldToAtlasUVMatrixArray = new Matrix4x4[validCount];
            }
            int index = 0;
            foreach (SliceData data in SliceDataList) {
                if (!data.ShouldRender) continue;
                worldToAtlasUVMatrixArray[index++] = data.sliceDataPerFrame.worldToAtlasUVMatrix;
            }
            return worldToAtlasUVMatrixArray;
        }
    }
    Matrix4x4[] worldToAtlasUVMatrixArray;

    /// <summary>
    /// Commonly used in DrawMeshInstanced
    /// </summary>
    public Matrix4x4[] FrustumLocalToWorldMatrixArray {
        get {
            int validCount = ValidSliceCount;
            if (frustumLocalToWorldMatrixArray == null || frustumLocalToWorldMatrixArray.Length != validCount) {
                frustumLocalToWorldMatrixArray = new Matrix4x4[validCount];
            }
            int index = 0;
            foreach (SliceData data in SliceDataList) {
                if (!data.ShouldRender) continue;
                frustumLocalToWorldMatrixArray[index++] = data.sliceDataPerFrame.frustumLocalToWorldMatrix;
            }
            return frustumLocalToWorldMatrixArray;
        }
    }
    Matrix4x4[] frustumLocalToWorldMatrixArray;

    public Vector4[] SliceUVOffsetExtendArray {
        get {
            int validCount = ValidSliceCount;
            if (sliceUVOffsetExtendArray == null || sliceUVOffsetExtendArray.Length != validCount) {
                sliceUVOffsetExtendArray = new Vector4[validCount];
            }
            int index = 0;
            foreach (SliceData data in SliceDataList) {
                if (!data.ShouldRender) continue;
                sliceUVOffsetExtendArray[index++] = data.sliceDataPerFrame.sliceUVOffsetExtend;
            }
            return sliceUVOffsetExtendArray;
        }
    }
    Vector4[] sliceUVOffsetExtendArray;

    public bool ShouldEvenRender {
        get {
            return Settings.maxObjects != 0 && AtlasResolution != Vector2Int.zero;
        }
    }

    #endregion


    /// <summary>
    /// Inited in PerObjectShadowPass's ctor
    /// </summary>
    public PerObjectShadowImpl(PerObjectShadowSettings settings) {

        this.Settings = settings;

        SliceDataList = new List<SliceData>(settings.maxObjects);
        for (int i = 0; i < settings.maxObjects; ++i) {
            SliceDataList.Add(new SliceData());
        }
    }

    #region API

    /// <summary>
    /// Clear all registered gameobjects.
    /// </summary>
    public void Clear() {
        for (int i = 0; i < SliceDataList.Count; ++i) {
            SliceDataList[i].Invalidate();
        }
    }

    public void RegisterGameObjectsByTag(string tag, bool testIfUpperMost = true) {
        try {
            GameObject[] gameObjects = GameObject.FindGameObjectsWithTag(tag);
            foreach (GameObject go in gameObjects) {
                RegisterGameObject(go);
            }
        } catch (System.Exception) {

        }

    }

    public void RegisterGameObject(GameObject gameObject) {

        if (gameObject == null) {
            throw new System.ArgumentNullException();
        }

        // check if already registered.
        foreach (SliceData data in SliceDataList) {
            if (data.gameObject == gameObject) {
                return;
            }
        }

        // check again to find empty slot to register
        int i = 0;
        for (; i < SliceDataList.Count; ++i) {
            if (!SliceDataList[i].IsValid) {
                SliceDataList[i].SetGameObject(gameObject);
                break;
            }
        }
        if (i == SliceDataList.Count) {
            // Debug.LogWarning("PerObjectShadowHelper::RegisterObject: already reaches size limit when registering new object: " + gameObject.name);
            // todo: FIFO
            return;
        }
    }

    public void RemoveGameObject(GameObject gameObject) {
        for (int i = 0; i < SliceDataList.Count; ++i) {
            if (SliceDataList[i].gameObject == gameObject) {
                SliceDataList[i].Invalidate();
                break;
            }
        }
    }

    /// <summary>
    /// Update atlas property for this pass.
    /// In URP, this should be called inside Configure()
    /// </summary>
    public void SetupAtlasProperty() {
        // do some res changes...
        AtlasResolution = new Vector2Int((int)Settings.sliceMaxResolution * SliceDataList.Count, (int)Settings.sliceMaxResolution);
    }

    /// <summary>
    /// Update shadow data for this frame.
    /// In URP, this should be called inside Execute()
    /// </summary>
    /// <param name="camera"></param>
    public void SetupRenderingData(Camera camera) {

        UpdateSliceData(camera);

    }


    #endregion

    void UpdateSliceData(Camera camera) {

        Profiler.BeginSample("UpdateSliceData");

        if (Settings.frustumCulling) {
            GeometryUtility.CalculateFrustumPlanes(camera, globalCameraFrustumPlanes);
        }

        foreach (SliceData data in SliceDataList) {
            if (!data.IsValid) {
                continue;
            }

            UpdateSliceData_RetrieveSceneData(data);

            UpdateSliceData_BeforeCulling(ref data.sliceDataPerFrame, Settings.LightRotation, Settings.padding, Settings.frustumExtend);

            if (Settings.frustumCulling) {
                UpdateSliceData_Culling(ref data.sliceDataPerFrame, globalCameraFrustumPlanes);
            }

            if (data.ShouldRender) {
                // not culled
                UpdateSliceData_AdaptiveRes(ref data.sliceDataPerFrame, camera, Settings.sliceMaxResolution, Settings.sliceMinResolution, Settings.adaptiveResRemapFactor, Settings.globalResScaleFactor);
            }
        }

        UpdateSliceData_AtlasPacking();

        foreach (SliceData data in SliceDataList) {
            if (!data.ShouldRender) {
                continue;
            }
            UpdateSliceData_PrepareResolveData(ref data.sliceDataPerFrame, AtlasResolution);
        }

        Profiler.EndSample();
    }

    /*
    [BurstCompile(CompileSynchronously = true)]
    public struct UpdateSliceDataJob : IJob {

        public SliceData.SliceDataPerFrame data;

        // retrived from settings:
        [ReadOnly] public Quaternion lightRotation;
        [ReadOnly] public float padding;
        [ReadOnly] public float frustumExtend;
        [ReadOnly] public bool frustumCulling;
        //public PerObjectShadowSettings.SliceResolution sliceMaxResolution;
        //public PerObjectShadowSettings.SliceResolution sliceMinResolution;
        //public int adaptiveResRemapFactor;
        //public float globalResScaleFactor;


        public void Execute() {
            UpdateSliceData_BeforeCulling(ref data, lightRotation, padding, frustumExtend);
        }
    }


    void UpdateSliceDataUsingJobs(Camera camera) {

        Profiler.BeginSample("UpdateSliceDataUsingJobs");

        if (Settings.frustumCulling) {
            GeometryUtility.CalculateFrustumPlanes(camera, globalCameraFrustumPlanes);
        }

        // Job Pass 1

        List<UpdateSliceDataJob> jobs = new List<UpdateSliceDataJob>();
        List<JobHandle> jobHandles = new List<JobHandle>();
        foreach (SliceData data in SliceDataList) {
            if (!data.IsValid) {
                continue;
            }
            UpdateSliceData_RetrieveSceneData(data);

            UpdateSliceDataJob job = new UpdateSliceDataJob {
                data = data.sliceDataPerFrame,
                padding = Settings.padding,
                frustumExtend = Settings.frustumExtend,
                frustumCulling = Settings.frustumCulling,
            };
            jobs.Add(job);
            jobHandles.Add(job.Schedule());
        }
        int index = 0;
        foreach (SliceData data in SliceDataList) {
            if (!data.IsValid) {
                continue;
            }
            jobHandles[index].Complete();
            data.sliceDataPerFrame = jobs[index].data;
            ++index;
        }

        // Job Pass 1 END

        foreach (SliceData data in SliceDataList) {
            if (Settings.frustumCulling) {
                UpdateSliceData_Culling(ref data.sliceDataPerFrame, globalCameraFrustumPlanes);
            }
            if (data.ShouldRender) {
                // not culled
                UpdateSliceData_AdaptiveRes(ref data.sliceDataPerFrame, camera, Settings.sliceMaxResolution, Settings.sliceMinResolution, Settings.adaptiveResRemapFactor, Settings.globalResScaleFactor);
            }

        }

        UpdateSliceData_AtlasPacking();

        foreach (SliceData data in SliceDataList) {
            if (!data.ShouldRender) {
                continue;
            }
            UpdateSliceData_PrepareResolveData(ref data.sliceDataPerFrame, AtlasResolution);
        }

        Profiler.EndSample();

    }
    */

    void UpdateSliceData_RetrieveSceneData(SliceData data) {
        data.sliceDataPerFrame.boundsWS = GetObjectBoundsInWorldSpace(data.renderers);
    }


    // curr impl: arrange alongside X, sorted from large to small
    // sorted array will be directly applied to working copy
    // todo: wondering should we pack like ue4 (2d bin packing), since gpu L2 & texture cache is so small and bin packing will do a lot of calcs.
    void UpdateSliceData_AtlasPacking() {
        // SliceDataList.Sort((a, b) => b.sliceResolution.y.CompareTo(a.sliceResolution.y));

        int l = 0;
        foreach (SliceData data in SliceDataList) {
            if (!data.ShouldRender) {
                continue;
            }
            data.sliceDataPerFrame.sliceOffset = new Vector2Int(l, 0); // we dont care about Y since we packed them alongside X
            l += data.sliceDataPerFrame.sliceResolution.x;
        }
    }


    #region Debug
    // since this class is not inherited from MonoBehaviour, a wrapper MonoBehaviour class can forward its OnDrawGizmos call to this function.
    public void DrawBoundsGizmos() {

        foreach (SliceData sliceData in SliceDataList) {
            if (!sliceData.ShouldRender) {
                continue;
            }
            Color ogColor = Gizmos.color;
            Gizmos.color = Color.yellow;
            Matrix4x4 ogMatrix = Gizmos.matrix;
            Gizmos.matrix = sliceData.sliceDataPerFrame.shadowViewToWorldMatrixForBounds;
            Gizmos.DrawWireCube(sliceData.sliceDataPerFrame.boundsSVS.center, sliceData.sliceDataPerFrame.boundsSVS.size);
            Gizmos.DrawWireSphere(sliceData.sliceDataPerFrame.boundsSVS.center, 0.05f);
            Gizmos.matrix = ogMatrix;
            Gizmos.DrawSphere(sliceData.sliceDataPerFrame.nearPlaneCenterWS, 0.05f);
            Gizmos.color = ogColor;
        }
    }

    public void DrawFrustumCubeGizmos() {
        foreach (SliceData sliceData in SliceDataList) {
            if (!sliceData.ShouldRender) {
                continue;
            }
            Color ogColor = Gizmos.color;
            Gizmos.color = Color.white;
            Matrix4x4 ogMatrix = Gizmos.matrix;

            Gizmos.matrix = sliceData.sliceDataPerFrame.frustumLocalToWorldMatrix;
            Gizmos.DrawMesh(FrustumCube);

            Gizmos.matrix = ogMatrix;
            Gizmos.DrawSphere(sliceData.sliceDataPerFrame.nearPlaneCenterWS, 0.05f);

            Gizmos.color = ogColor;
        }
    }
    #endregion



}
