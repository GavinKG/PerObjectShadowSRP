# Per Object Shadow Implementation using Unity SRP



## 简介

该项目使用 Unity SRP 实现逐物件阴影。此种阴影解决方案主要适用于场景静态光源下绘制动态物体阴影（可以参考 UE4 的实现），也可以用来在后处理阶段直接 Blend 到屏幕上实现假阴影（类似于 Unlit 贴花），供低端移动设备使用。

由于静态平行光源下的静态物体均可在离线状态下烘焙阴影到 Lightmap（Shadow mask），此时若使用 CSM 渲染余下的少量动态物体将会造成 Shadowmap 的浪费。使用逐物件阴影解决方案会得到比较显著的 GPU 性能提升。

绘制简易流程图如下：

```
Pass #1: Per Object Shadow Pass
[] -> [Per Object Shadow Atlas (shadowmap texture)]
	for each moveable object 
		draw depth to corresponding slice
		
Pass #2: Resolve Pass
[Scene Depth] -> [Screen-space Shadow Map] or [Scene Final Color]
	for each caster's shadow frustum
		resolve light attenuation (shadowed) to output color attachment
```

// todo: 补图（原理/预览）

*当前该项目仍积极开发中。*



## 功能和优化

* 项目基于 SRP 开发，支持 URP 的 Renderer Feature 添加自定义 Pass，并未改动 SRP 管线源代码，可直接集成到 URP 中，也可以进行少量移植操作实现在自定义 SRP 中。
* 在未改动的 URP 中，只需添加一个逐物件阴影管理单例类即可无感化使用逐物件阴影功能，在自定义管线中将上述单例类直接实现在管线中 `ScriptableRenderer` 的继承类。同时，为了减少场景的查询开销，该解决方案也提供物体的注册/解除注册接口，允许手动控制需要绘制的物体。
* 绘制 Screen-space Shadow Map（或直接绘制在场景最终颜色上实现假阴影）时，Resolve Pass 使用 GPU Instancing 绘制，减少 Draw Call。
* 物体会根据屏幕空间占比动态调整其在阴影 atlas 纹理（`PerObjectShadowAtlas`）上的大小，尽量保证阴影 texel 在屏幕占比恒定，并提升性能。所有 slice 在 atlas 纹理上紧密排列，可能会减少 Texture Cache Miss（有待验证）。
* 支持主流平台和图形 API，目前已经在支持 Direct3D, OpenGL / GLES3+, Vulkan 图形 API 的 PC 和 Android 平台上测试通过（Metal 和 macOS / iOS 有待测试）。



## 使用说明

### 配置

// todo

### 源代码文件用途

在 `Assets/PerObjectShadow/Scripts` 下：

* `PerObjectShadowSettings.cs`：配置信息。将会暴露在 Renderer 的设置中（URP 将会暴露在 `PerObjectShadowPass` 的 Renderer Feature 中）。
* `PerObjectShadowImpl.cs`：具体实现。其中，`PerObjectShadowImpl` 类为具体 RP 无关的实现类，该类内部信息将在渲染流程的多个 Pass 之间共享，以节省重复计算所造成的性能损失。
* `PerObjectShadowImpl.Utils.cs`：具体实现类中的静态成员（属性/方法）。
* `PerObjectShadowHelper.cs`：单例管理类。其中，`PerObjectShadowHelper` 为继承自 `MonoBehaviour` 的单例类，其中只包含上述 `PerObjectShadowImpl` 类实例的引用用来在多个 Pass 之间共享数据，以及 visualize / debugging。需要添加到场景中的一个（空）GameObject 上。
* `PerObjectShadowPass.cs`：Per-object Shadow Pass 实现。该 Pass 驱动 `PerObjectShadowImpl` 进行计算，并获取计算结果进行阴影 atlas 的绘制。
* `PerObjectShadowRendererFeature.cs`：上述 Pass 对应的 URP Renderer Feature，需结合 URP 使用。
* `PerObjectShadowResolvePass.cs`：阴影解算 Pass，该 Pass 将阴影 atlas 解算到屏幕空间，形成屏幕空间 shadowmap；或直接绘制到场景颜色中，实现假阴影。
* `PerObjectShadowResolveRendererFeature.cs`：上述 Pass 对应的 URP Renderer Feature，需结合 URP 使用。

在 `Assets/PerObjectShadow/Shaders` 下：

* `PerObjectShadowResolve.shader`：阴影解算 Pass 中 Frustum 所使用的着色器，负责进行具体的阴影解算操作。

### 使用流程

// todo

### 示例工程列表

// todo



## todos

### Project related

- [ ] Editor 面板
- [ ] 支持场景 main directional light
- [ ] Distance Fade

### README related

