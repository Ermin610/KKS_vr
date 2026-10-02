# KKS_vr

KKS CharaStudio 的 VR 功能插件，基于 Ermin610 的 KK_VR 移植，使用现有 KKS VR 的 VRGIN_OpenXR、Unity XR OpenVR Loader 和 SteamVR Action 输入。

当前版本：**0.4.0-preview2**。核心功能及 CameraSync 已完成编译和离线回归，尚未完成头显实测。适用于 KKS CharaStudio。

## 获取源码

```powershell
git clone https://github.com/Ermin610/KKS_vr.git
cd KKS_vr
```

核心、CameraSync、原生 ReShade 控制桥和测试在同一仓库，无需克隆其他源码仓库或初始化子模块。私有仓库需要使用已获授权的 GitHub 账号。

## 构建

需要 Windows、.NET 9 SDK、Visual Studio 2022 C++ 工具链（v143 和 Windows SDK），以及安装了 BepInEx 5、KKSAPI 和 KKS VR 运行组件的本地 KKS。构建会从 NuGet 恢复 .NET Framework 引用包。

将下面路径替换为你的游戏目录：

```powershell
.\build-kks.ps1 -GameDir 'D:\Games\KKS'
```

输出：`output/KKS-VR-Overhaul-0.4.0-preview2.zip`。完整安装、回退和验证范围见 [兼容说明](docs/KKS-COMPATIBILITY.md)。脚本只生成安装包，不自动安装。

只编译 C# 或使用 IDE 时，可打开 `KKS_vr.sln`，通过环境变量指定引用目录：

```powershell
$env:KKS_GAME_DIR = 'D:\Games\KKS'
dotnet build KKS_vr.sln -c Release
```

也可使用 `dotnet build KKS_vr.sln -c Release -p:KKSGameDir='D:\Games\KKS'`。解决方案包含核心、CameraSync 和离线测试；原生桥由 `build-kks.ps1` 单独构建。游戏程序集仅从本地读取，不在仓库或安装包中分发。

## 测试

无需启动游戏的检查：

```powershell
dotnet run --project tests/KksOffline/KksOffline.csproj -c Release
.\scripts\Test-KksInstaller.ps1
```

输入／Timeline 回归直接编译生产源码，使用模拟的 Unity／SteamVR 接口；安装器测试只操作 `output/installer-regression-*` 中的模拟目录。preview2 已通过 239 项输入／相机断言和 85 项安装／打包断言。这些检查不能代替头显实测。

游戏内检查会临时替换插件并启动空白 Studio，请在需要验证时退出 Studio 后运行：

```powershell
.\scripts\Test-KksRuntime.ps1 -GameDir 'D:\Games\KKS'
```

## 目录

| 路径 | 内容 |
| --- | --- |
| `KKSCharaStudioVRPlugin/` | KKS 入口、XR 启动、SteamVR 输入和平台适配 |
| `KKCharaStudioVRPlugin/` | 从 KK 移植的共用功能源码和资源，保留命名空间以兼容反射调用 |
| `CameraSync/` | KKS 相机同步、Timeline 跟随和腕部菜单接口 |
| `native/KKVRReShadeBridge/` | ReShade 控制桥及所需 SDK 头文件 |
| `tests/KksOffline/` | 无游戏依赖的输入／Timeline 回归 |
| `scripts/` | 安装、回退与验证脚本 |
| `docs/` | 移植说明、验证范围及限制 |

当前源码从原仓库的本地 KKS 适配工作区迁入，包含 preview2 的打磨修改。后续 KKS 开发直接在本仓库进行；来源与许可见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。
