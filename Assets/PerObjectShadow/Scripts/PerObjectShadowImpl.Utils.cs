// Gavin_KG presents

using System.Collections;
using System.Collections.Generic;
using UnityEngine.Profiling;
using UnityEngine;

// All static functions / properties goes here
// UpdateSliceData functions are marked with static to be used by job system.
public partial class PerObjectShadowImpl {

    // shader property names used by SetGlobalXXX
    public const string s_PerObjectShadowAtlas = "_PerObjectShadowAtlas"; // the texture
    public const string s_WorldToUVMatrix = "_WorldToUVMatrix";
    public const string s_SliceUVOffsetExtend = "_SliceUVOffsetExtend";
    public const string s_TexelSize = "_PerObjectShadowAtlasTexelSize";
    public const string s_ScreenSpaceShadowmap = "_ScreenSpaceShadowMap";


    // Global temp object & object pool, optimizing per-frame ops.
    static readonly Vector3[] globalTempCubeCorners = new Vector3[8];
    static readonly Plane[] globalCameraFrustumPlanes = new Plane[6];


    #region Static Data

    // origins at the center of "near" face, can be scale (only) to form a frustum (directional light only)
    // todo: change to const  (kill the new op)
    public static Mesh FrustumCube {
        get {
            if (frustumCube == null) {

                frustumCube = new Mesh {
                    name = "Frustum Cube"
                };

                // unity use CW
                var verts = new Vector3[] {
                    // left
                    new Vector3(-0.5f, 0.5f, 1f),
                    new Vector3(-0.5f, 0.5f, 0f),
                    new Vector3(-0.5f, -0.5f, 0f),
                    new Vector3(-0.5f, -0.5f, 1f),
                    // right
                    new Vector3(0.5f, 0.5f, 1f),
                    new Vector3(0.5f, 0.5f, 0f),
                    new Vector3(0.5f, -0.5f, 0f),
                    new Vector3(0.5f, -0.5f, 1f),
                    // up
                    new Vector3(-0.5f, 0.5f, 1f),
                    new Vector3(0.5f, 0.5f, 1f),
                     new Vector3(0.5f, 0.5f, 0f),
                     new Vector3(-0.5f, 0.5f, 0f),
                    // down
                    new Vector3(-0.5f, -0.5f, 1f),
                    new Vector3(0.5f, -0.5f, 1f),
                    new Vector3(0.5f, -0.5f, 0f),
                    new Vector3(-0.5f, -0.5f, 0f),
                    // near
                    new Vector3(-0.5f, 0.5f, 0f),
                    new Vector3(0.5f, 0.5f, 0f),
                    new Vector3(0.5f, -0.5f, 0f),
                    new Vector3(-0.5f, -0.5f, 0f),
                    // far
                    new Vector3(-0.5f, 0.5f, 1f),
                    new Vector3(0.5f, 0.5f, 1f),
                    new Vector3(0.5f, -0.5f, 1f),
                    new Vector3(-0.5f, -0.5f, 1f),
                };
                frustumCube.vertices = verts;

                var indices = new int[] {
                    0,
                    1,
                    2,
                    0,
                    2,
                    3, // left
                    4,
                    6,
                    5,
                    4,
                    7,
                    6, // right
                    8,
                    9,
                    10,
                    8,
                    10,
                    11, // up
                    12,
                    14,
                    13,
                    12,
                    15,
                    14, // down
                    16,
                    17,
                    18,
                    16,
                    18,
                    19, // near
                    20,
                    22,
                    21,
                    20,
                    23,
                    22, // far

                };
                frustumCube.triangles = indices;

#if UNITY_EDITOR
                // for debugging purposes
                frustumCube.RecalculateBounds();
                frustumCube.RecalculateNormals();
                frustumCube.RecalculateTangents();
#endif

                frustumCube.UploadMeshData(true);

            }
            return frustumCube;
        }
    }
    static Mesh frustumCube;

    public static Vector3[] FrustumCubeCorners {
        get {
            if (frustumCubeCorners == null) {
                frustumCubeCorners = new Vector3[] {
                    // left
                    new Vector3(-0.5f, 0.5f, 1f),
                    new Vector3(-0.5f, 0.5f, 0f),
                    new Vector3(-0.5f, -0.5f, 0f),
                    new Vector3(-0.5f, -0.5f, 1f),
                    // right
                    new Vector3(0.5f, 0.5f, 1f),
                    new Vector3(0.5f, 0.5f, 0f),
                    new Vector3(0.5f, -0.5f, 0f),
                    new Vector3(0.5f, -0.5f, 1f),
                };
            }
            return frustumCubeCorners;
        }

    }
    static Vector3[] frustumCubeCorners;

    #endregion


    static void UpdateSliceData_BeforeCulling(ref SliceData.SliceDataPerFrame data, Quaternion lightRotation, float padding, float frustumExtend) {

        Profiler.BeginSample("UpdateSliceData_BeforeCulling");

        // encapsulated bounds in shadow view space
        Matrix4x4 worldToShadowViewMatrixForBounds = GetWorldToShadowViewMatrix(data.boundsWS.center, lightRotation);
        data.shadowViewToWorldMatrixForBounds = worldToShadowViewMatrixForBounds.inverse;
        data.boundsSVS = GetEncapsulatedBoundsInSpace(data.boundsWS, worldToShadowViewMatrixForBounds);

        // shadow view matrix 
        Vector3 nearPlaneCenterSVS = GetBoundsBackCenterPosition(data.boundsSVS);
        data.nearPlaneCenterWS = data.shadowViewToWorldMatrixForBounds.MultiplyPoint3x4(nearPlaneCenterSVS);
        data.shadowViewMatrix = GetWorldToShadowViewMatrix(data.nearPlaneCenterWS, lightRotation);

        // shadow projection matrix (padding applied)
        data.shadowProjMatrix = GetShadowViewToClipMatrix(data.boundsSVS.extents + new Vector3(padding, padding, 0));

        // frustum model matrix [shadowViewMatrx^-1][scale]. frustum model uses Mesh FrustumCube.
        data.frustumLocalToWorldMatrix = data.shadowViewMatrix.inverse;
        Vector3 scale = data.boundsSVS.size;
        scale.z += frustumExtend;
        data.frustumLocalToWorldMatrix.m00 *= scale.x; //
        data.frustumLocalToWorldMatrix.m01 *= scale.y; //
        data.frustumLocalToWorldMatrix.m02 *= scale.z; //
        data.frustumLocalToWorldMatrix.m10 *= scale.x; // 
        data.frustumLocalToWorldMatrix.m11 *= scale.y; // xyz scale (right mul)
        data.frustumLocalToWorldMatrix.m12 *= scale.z; // 
        data.frustumLocalToWorldMatrix.m20 *= scale.x; //
        data.frustumLocalToWorldMatrix.m21 *= scale.y; //
        data.frustumLocalToWorldMatrix.m22 *= scale.z; //

        Profiler.EndSample();
    }

    static void UpdateSliceData_Culling(ref SliceData.SliceDataPerFrame data, Plane[] frustumPlanes) {

        Profiler.BeginSample("UpdateSliceData_Culling");

        Bounds frustumBoundsWS = GetEncapsulatedBoundsInSpace(FrustumCubeCorners, data.frustumLocalToWorldMatrix);
        data.disabled = !GeometryUtility.TestPlanesAABB(frustumPlanes, frustumBoundsWS); // TestPlanesAABB can only be called from the main thread...should use custom impl to work in multithread env.

        Profiler.EndSample();
    }

    static void UpdateSliceData_AdaptiveRes(ref SliceData.SliceDataPerFrame data, Camera camera, PerObjectShadowSettings.SliceResolution sliceMaxResolution, PerObjectShadowSettings.SliceResolution sliceMinResolution, int adaptiveResRemapFactor, float globalResScaleFactor) {

        // we favor ue4's approach: use screen percentage (dont care whether it is actually visible or not) from object itself instead of shadow frustum.
        // since direct lerp between min and max resolution based on screen percentage will cause shadow to be unstable when camera is moving around,
        // we devide the whole range into segments like (e.g.: max = 256, min = 64):
        // segment:       0     1     2     3
        //     res:   [64-- 128-- 192-- 256-- ]
        // %screen:    0%   25%   50%   75%   100%

        Profiler.BeginSample("UpdateSliceData_AdaptiveRes");

        int maxRes = (int)sliceMaxResolution;
        int minRes = (int)sliceMinResolution;
        if (minRes > maxRes) {
            throw new System.Exception("Slice min resolution is greater that max resolution. Go ahead and change the settings.");
        }

        int sliceLongerRes;
        if (minRes == maxRes) {
            sliceLongerRes = maxRes;
        } else {
            float screenPercentage = GetScreenPercentage(data.boundsWS, camera);

            // screenPercentage *= screenPercentage; // remap to match resolution change
            screenPercentage = 1 - screenPercentage;
            for (int i = 0; i < adaptiveResRemapFactor; ++i) {
                screenPercentage *= screenPercentage;
            }
            screenPercentage = 1 - screenPercentage; // remap to get better result

            int segmentCount = maxRes / minRes;
            int segmentIndex = (int)(screenPercentage * segmentCount);
            if (segmentCount == segmentIndex) {
                --segmentIndex; // when screenPercentage = 1.0
            }
            sliceLongerRes = segmentIndex * minRes + minRes;
        }

        Vector2 boundsSVSSize = data.boundsSVS.size;
        float aspectRatio = boundsSVSSize.x / boundsSVSSize.y;
        if (aspectRatio >= 1.0) {
            data.sliceResolution = new Vector2Int(sliceLongerRes, (int)(sliceLongerRes / aspectRatio)); // wide 
        } else {
            data.sliceResolution = new Vector2Int((int)(sliceLongerRes * aspectRatio), sliceLongerRes); // tall
        }

        data.sliceResolution.x = (int)(data.sliceResolution.x * globalResScaleFactor);
        data.sliceResolution.y = (int)(data.sliceResolution.y * globalResScaleFactor);

        // data.sliceResolution = new Vector2Int((int)Settings.sliceMaxResolution, (int)Settings.sliceMaxResolution); // old method, completely ignore adaptive res.

        Profiler.EndSample();
    }


    static void UpdateSliceData_PrepareResolveData(ref SliceData.SliceDataPerFrame data, Vector2Int atlasResolution) {

        Profiler.BeginSample("UpdateSliceData_PrepareResolveData");

        // world to atlas uv matrix: [UV->slice][clip->UV][proj][view] baked together (ignore z)
        Matrix4x4 apiAwareProjMatrix = GL.GetGPUProjectionMatrix(data.shadowProjMatrix, true); // data.shadowProjMatrix is not api-aware. see GetShadowViewToClipMatrix() for details.
        data.worldToAtlasUVMatrix = apiAwareProjMatrix * data.shadowViewMatrix;
        // data.worldToAtlasUVMatrix = Matrix4x4.Scale(new Vector3(0.5f, 0.5f, 1)) * Matrix4x4.Translate(new Vector3(1f, 1f, 0)) * data.worldToAtlasUVMatrix; // todo: bake in

        data.worldToAtlasUVMatrix.m00 = (0.5f * (data.worldToAtlasUVMatrix.m00 + data.worldToAtlasUVMatrix.m30)); // 
        data.worldToAtlasUVMatrix.m01 = (0.5f * (data.worldToAtlasUVMatrix.m01 + data.worldToAtlasUVMatrix.m31)); // 
        data.worldToAtlasUVMatrix.m02 = (0.5f * (data.worldToAtlasUVMatrix.m02 + data.worldToAtlasUVMatrix.m32)); // 
        data.worldToAtlasUVMatrix.m03 = (0.5f * (data.worldToAtlasUVMatrix.m03 + data.worldToAtlasUVMatrix.m33)); // [clip->UV]
        data.worldToAtlasUVMatrix.m10 = (0.5f * (data.worldToAtlasUVMatrix.m10 + data.worldToAtlasUVMatrix.m30)); // 
        data.worldToAtlasUVMatrix.m11 = (0.5f * (data.worldToAtlasUVMatrix.m11 + data.worldToAtlasUVMatrix.m31)); // 
        data.worldToAtlasUVMatrix.m12 = (0.5f * (data.worldToAtlasUVMatrix.m12 + data.worldToAtlasUVMatrix.m32)); // 
        data.worldToAtlasUVMatrix.m13 = (0.5f * (data.worldToAtlasUVMatrix.m13 + data.worldToAtlasUVMatrix.m33)); // 

        // todo: in opengl we should scale z as well. (or should we)

        // UV range
        Vector2 offset = data.sliceOffset;
        Vector2 extend = data.sliceResolution;
        Vector2 rtSize = atlasResolution;
        offset /= rtSize;
        extend /= rtSize; // scale to uv 0~1 range
        data.sliceUVOffsetExtend = new Vector4(offset.x, offset.y, extend.x, extend.y);

        Vector2 sliceScale = new Vector2((float)data.sliceResolution.x / atlasResolution.x, (float)data.sliceResolution.y / atlasResolution.y);

        // whole range UV -> Slice
        data.worldToAtlasUVMatrix = Matrix4x4.Translate(new Vector3(data.sliceUVOffsetExtend.x, data.sliceUVOffsetExtend.y, 0)) * Matrix4x4.Scale(new Vector3(sliceScale.x, sliceScale.y, 1)) * data.worldToAtlasUVMatrix; // todo: bake in

        Profiler.EndSample();
    }


    #region Utilities

    /// <summary>
    /// Note that this implementation does not care whether the object is in view or not.
    /// e.g.: An object behind the camera (invisible) can get the result of 1.0 when placed close enough to the camera.
    /// This is perfect for calculating shadow resolution since object behind camera can still cast shadow that might have huge footprint on screen.
    /// </summary>
    /// 
    static float GetScreenPercentage(Bounds boundsWS, Camera camera) {
        GetCornersFromBounds(boundsWS, globalTempCubeCorners);
        float minX = 1, minY = 1, maxX = 0, maxY = 0;

        for (int i = 0; i < 8; ++i) {
            Vector3 p = camera.WorldToViewportPoint(globalTempCubeCorners[i]);
            if (p.x < minX) {
                minX = p.x;
            } else if (p.x > maxX) {
                maxX = p.x;
            }
            if (p.y < minY) {
                minY = p.y;
            } else if (p.y > maxY) {
                maxY = p.y;
            }
        }

        minX = Mathf.Max(minX, 0);
        minY = Mathf.Max(minY, 0);
        maxX = Mathf.Min(maxX, 1);
        maxY = Mathf.Min(maxY, 1);

        return (maxX - minX) * (maxY - minY);
    }

    static Vector3 GetBoundsBackCenterPosition(Bounds b) {
        return b.center + Vector3.back * b.extents.z;
    }

    static Matrix4x4 GetWorldToShadowViewMatrix(Vector3 centerWS, Quaternion lightRotation) {
        // in reverse
        return Matrix4x4.Rotate(Quaternion.Inverse(lightRotation)) * Matrix4x4.Translate(-centerWS);
    }

    static Matrix4x4 GetShadowViewToClipMatrix(Vector3 extents) {
        Matrix4x4 ortho = Matrix4x4.Ortho(-extents.x, extents.x, -extents.y, extents.y, 0f, extents.z * 2f);
        ortho.m02 *= -1; // 
        ortho.m12 *= -1; // custom view matrix does not embed z-flip, so cancel that
        ortho.m22 *= -1; //
        ortho.m32 *= -1; //
        // ortho = GL.GetGPUProjectionMatrix(ortho, true); // not needed when using cmd.SetViewProjectionMatrices instead of passing the matrix directly.
        return ortho;
    }

    static Bounds GetObjectBoundsInWorldSpace(Renderer[] renderers) {

        if (renderers.Length == 0) {
            return new Bounds(); // centers at origin, zero volume
        }
        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; ++i) { // iterate over rest renderers
            bounds.Encapsulate(renderers[i].bounds);
        }
        return bounds;
    }

    static void GetCornersFromBounds(Bounds bounds, Vector3[] outCorners) {
        if (outCorners.Length != 8) {
            return;
        }
        Vector3 max = bounds.max, min = bounds.min;

        outCorners[0] = max;
        outCorners[1] = min;
        outCorners[2] = new Vector3(min.x, max.y, min.z);
        outCorners[3] = new Vector3(min.x, max.y, max.z);
        outCorners[4] = new Vector3(max.x, max.y, min.z);
        outCorners[5] = new Vector3(min.x, min.y, min.z);
        outCorners[6] = new Vector3(min.x, min.y, max.z);
        outCorners[7] = new Vector3(max.x, min.y, min.z);
    }

    static Bounds GetEncapsulatedBoundsInSpace(Bounds bounds, Matrix4x4 transformMatrix) {
        GetCornersFromBounds(bounds, globalTempCubeCorners);
        return GetEncapsulatedBoundsInSpace(globalTempCubeCorners, transformMatrix);
    }

    static Bounds GetEncapsulatedBoundsInSpace(Vector3[] corners, Matrix4x4 transformMatrix) {

        if (corners.Length != 8) {
            throw new System.Exception("Hey, a cube has 8 corners.");
        }

        // apply transformMatrix
        Vector3 max_ = transformMatrix.MultiplyPoint3x4(corners[0]);
        Vector3 min_ = transformMatrix.MultiplyPoint3x4(corners[1]);
        Vector3 up1_ = transformMatrix.MultiplyPoint3x4(corners[2]);
        Vector3 up2_ = transformMatrix.MultiplyPoint3x4(corners[3]);
        Vector3 up3_ = transformMatrix.MultiplyPoint3x4(corners[4]);
        Vector3 lo1_ = transformMatrix.MultiplyPoint3x4(corners[5]);
        Vector3 lo2_ = transformMatrix.MultiplyPoint3x4(corners[6]);
        Vector3 lo3_ = transformMatrix.MultiplyPoint3x4(corners[7]);

        // encapsulate

        /*
        Bounds newBounds = new Bounds(max_, Vector3.zero);
        newBounds.Encapsulate(min_);
        newBounds.Encapsulate(up1_);
        newBounds.Encapsulate(up2_);
        newBounds.Encapsulate(up3_); // bounds.encapsulate has poor performance
        newBounds.Encapsulate(lo1_);
        newBounds.Encapsulate(lo2_);
        newBounds.Encapsulate(lo3_);
        */

        Vector3 bMin = max_;
        Vector3 bMax = max_;
        MakeMin(ref bMin, min_); MakeMax(ref bMax, max_);
        MakeMin(ref bMin, up1_); MakeMax(ref bMax, up1_);
        MakeMin(ref bMin, up2_); MakeMax(ref bMax, up2_);
        MakeMin(ref bMin, up3_); MakeMax(ref bMax, up3_);
        MakeMin(ref bMin, lo1_); MakeMax(ref bMax, lo1_);
        MakeMin(ref bMin, lo2_); MakeMax(ref bMax, lo2_);
        MakeMin(ref bMin, lo3_); MakeMax(ref bMax, lo3_);
        return new Bounds((bMin + bMax) / 2, bMax - bMin);
    }

    static void MakeMin(ref Vector3 src, in Vector3 vec) {
        src.x = Mathf.Min(src.x, vec.x);
        src.y = Mathf.Min(src.y, vec.y);
        src.z = Mathf.Min(src.z, vec.z);
    }

    static void MakeMax(ref Vector3 src, in Vector3 vec) {
        src.x = Mathf.Max(src.x, vec.x);
        src.y = Mathf.Max(src.y, vec.y);
        src.z = Mathf.Max(src.z, vec.z);
    }

    #endregion


}
