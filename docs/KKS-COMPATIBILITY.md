# KKS VR Overhaul 0.4.0 测试版 preview2

本包把 Ermin610/KK_VR 的工作室功能编译为 KKS 版本，使用现有 KKS 的 VRGIN_OpenXR、Unity XR OpenVR Loader 和 SteamVR Action 输入。适用范围是 **KKS CharaStudio**。

## 安装

前提：KKS、BepInEx 5、KKSAPI，以及已经安装的 KKS VR 运行组件。测试环境为 Unity 2019.4.9f1，原 KKS Chara Studio VR 配置版本 1.4.0。

1. 解压测试包并退出 Studio。
2. 在解压目录运行：

   ```powershell
   .\Install-KKS.ps1 -GameDir 'E:\KKS\KKS'
   ```

3. 启动 SteamVR，连接头显与控制器，再运行游戏目录中的 `StartKKSStudioVR.bat`。
4. 普通桌面模式使用 `StartKKSStudioNoVR.bat`。

请解压到游戏目录之外。安装器先验证包内文件的 SHA256，再备份要替换的文件，移除重复的工作室 VR／CameraSync DLL，最后安装本包。原主游戏 VR、XR 运行库、角色卡、场景卡和现有配置保留。备份位于游戏目录 `KKS-VR-Backups`，安装后会输出对应的恢复命令：

```powershell
.\Install-KKS.ps1 -GameDir 'E:\KKS\KKS' -Restore -BackupDirectory '安装时输出的备份目录'
```

KKS 设置使用独立的 `KKSCSVRSettings.xml`。CameraSync 配置 GUID 为 `yukyo.kksvr.camerasync`。

恢复前会检查备份完整性和安装后文件是否被改动，包括后来在原重复插件路径放入的新文件。发现冲突时会停止并显示路径，请先保存该文件。复制失败时安装器会尝试回退全部受影响文件；若文件锁阻止回退，会报告备份位置和失败原因。

## preview2 离线打磨

- 修正 KKS 输入适配的触摸抬起和组合按键边沿；断连时不读取残留按键／轴值、不发送震动。失去手柄追踪时结束当前抓取并重置移动基准。
- 修正 Timeline 在保留当前位置模式下反复暂停／恢复、相机交接时重复叠加偏移的问题。记录实际应用过的偏移，确保暂停期间修改或清除偏移、首次播放前设置偏移仍有效；更换 VR 原点后重新建立锚点。
- 捕获 XR 初始化／启动异常，即使停止失败也继续尝试释放运行组件。若 VRGIN 已创建原点后功能初始化失败，停用功能组件并记录错误，保留追踪运行组件直到退出，需根据日志修复后重启 Studio。
- 安装前和复制后校验文件，回退保护后续改动；打包拒绝游戏目录和混入无关文件的输出目录。

这一轮没有启动游戏或替换当前游戏插件。新的启动异常处理和追踪丢失后的实际抓取释放仍需 Unity／头显验证。

## 功能移植

- 共用 KK 的虚拟手、手指弯曲、DynamicBone 碰撞、近距离 IK 抓取、震动、摇杆移动、转向、高度和双手缩放逻辑。
- 共用腕部菜单、多语言、文件／卡片预览、角色替换、衣服／饰品操作与回退逻辑。
- KKS 的角色管理、加载状态、音频配置保存和截图接口通过独立兼容层接入。
- MMDD 的 IronPython 桥引用 KKS DLL 名称；角色 API、Accessory States 和 Coordinate Load Option 使用 KKS 的程序集／命名空间。
- 在 KKS VRGIN 上补入弹窗捕获、隐藏子物体 UI 层级和点击排序修正。
- 桌面遮罩改用 Unity XR API；手部材质使用 KKS 的着色器资源。
- KKS CameraSync 提供 Timeline 播放跟随、FOV 距离补偿、用户高度／偏航设置、仅动画模式及 MMDD 相机占用协调。Timeline 每帧从固定相机局部锚点重建 VR 原点，FOV 补偿设有边界。
- 随包提供编译后的 ReShade 控制桥。需另装支持 add-on 的 ReShade；KKS VR 画面中的 ReShade 效果仍需实机验证。

CameraSync 基于你仓库的 `fix/timeline-desktop-composition` 分支（`cf555c4`，0.2.0）增加 KKS 适配及腕部菜单接口。GitHub 可取得的源码与 KK README 提到的 0.2.3 不一致，本包不宣称复现了 0.2.3 的全部实现。

## 验证范围

已完成：

- KKS 核心、CameraSync、原生 ReShade 桥编译。
- 共用源码的 KK net35 编译回归检查，使用本机已有的 KK 引用文件。
- 初版在本机 KKS 中以 `--novr` 启动空白 Studio：插件加载、无 VR 原点创建、设置 XML 往返、手部着色器加载、KKSAPI／饰品／换装类型解析、Harmony 补丁安装、CameraSync 接口和 MMDD 程序集解析通过。preview2 未重跑游戏内检查。
- 初版 Unity 运行时中的镜头计算检查：一万个相同帧无累计位移、高度／偏航可复位、追踪位移保持相对关系。
- preview2 离线回归：直接编译输入适配和 Timeline 兼容源码，使用 Unity 数学及游戏／SteamVR 模拟接口，覆盖输入边沿、断连、震动、暂停恢复、偏移修改、相机交接和原点更换。模拟接口不能验证真实追踪、渲染或 SteamVR 绑定。
- preview2 安装器独立目录测试：安装／重复回退、损坏的包与备份、文件锁导致的中途失败、原路径被新文件占用、后续改动保护、依赖缺失和打包目录检查。

**当前是可安装测试版，尚未完成头显实测。** 空白 Studio 验证没有加载角色或动作，不能代替 VR 中的手柄按键、激光点击、碰撞、换装保留数据、MMDD／Timeline 播放和 ReShade 双眼画面验证。

建议首次头显测试使用场景副本：先检查左右手与菜单，再测试抓取和移动；随后加载一个角色验证换装及撤销；最后测试动作、相机、场景重载。当前 KKS 启动日志中存在原有 zipmod 错误，功能异常时应结合对应日志定位。

## 从源码构建与自动验证

核心和相机源码已合并到 `Ermin610/KKS_vr`，克隆一次后在仓库根目录运行（游戏路径按本机安装修改）：

```powershell
.\build-kks.ps1 -GameDir 'D:\Games\KKS'
dotnet run --project .\tests\KksOffline\KksOffline.csproj -c Release
.\scripts\Test-KksInstaller.ps1
.\scripts\Test-KksRuntime.ps1 -GameDir 'D:\Games\KKS'
```

构建需要 .NET SDK、.NET Framework 引用包和 VS 2022 C++ 工具链。安装的游戏 DLL 只作为编译引用，不随包分发。运行时验证脚本会临时替换工作室插件，在独立启动的空白 Studio 完成检查，然后恢复插件及其配置；结果保存在 `output/kks-validation-*`。

离线 C# 回归使用 .NET 9，不需要启动游戏，默认读取仓库内 `CameraSync` 源码；可通过 `-p:CameraSyncRepo=绝对路径` 指定另一位置的相机源码。安装器测试仅操作 `output/installer-regression-*` 下的模拟目录。默认构建输出为 `output/KKS-VR-Overhaul-0.4.0-preview2.zip`。

来源：核心 `Ermin610/KK_VR` 的 `54c53ae`；KKS XR 初始化和着色器参考 `Ermin610/KK_VR_STUDIO` 的 `5391709`。KKS 着色器附原项目 MIT 许可，CameraSync 附其原有许可。
