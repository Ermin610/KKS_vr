# 源码来源与第三方许可

此仓库汇集 KKS 适配所需的源码和资源。各组件保留原有许可；本说明不为未声明许可的核心代码另行授予许可。

- 共用功能源码及 ReShade 桥：来自 [Ermin610/KK_VR](https://github.com/Ermin610/KK_VR)，基于提交 `54c53ae915094d55e070938c2b3d3b246ba4cafe`，包含本地 KKS 适配与 preview2 修正。保留原始程序集及作者信息。
- KKS XR 接入参考和 `ColorZOrderShader` 资源：参考 [Ermin610/KK_VR_STUDIO](https://github.com/Ermin610/KK_VR_STUDIO) 提交 `5391709d25708203a658ce9e7eb2d27fc17ce126`。资源的 MIT 许可见 [LICENSE-KKS-VR](KKSCharaStudioVRPlugin/Resources/LICENSE-KKS-VR)。
- CameraSync：来自 [Ermin610/KK_VR_CameraSync](https://github.com/Ermin610/KK_VR_CameraSync) 的 `fix/timeline-desktop-composition` 分支，基于提交 `cf555c4837cb203cf5fce29b41910cf6e6978c9c`，原作者 YukyoMoe，增加 KKS 适配与 Timeline 修正。MIT 许可见 [CameraSync/LICENSE.txt](CameraSync/LICENSE.txt)。
- ReShade SDK 头文件：保留 [ReShade BSD 3-Clause 许可](native/KKVRReShadeBridge/LICENSE-RESHade.md)。
- 原项目随附的 KoikatuVR、VRGIN 及 ReShade 许可副本保存在 [ThirdPartyNotices](ThirdPartyNotices/)。

游戏、BepInEx、Unity、VRGIN_OpenXR 和可选插件 DLL 由开发者本地安装提供，不包含在此源码仓库中。
