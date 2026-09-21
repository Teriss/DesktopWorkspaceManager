# Desktop Workspace Manager

[English](README.md)

Desktop Workspace Manager 是一个面向 Windows 11 的原生桌面工作区管理器。它把虚拟桌面、物理显示器和顶层应用窗口放在同一个可视化界面中，适合同时运行开发工具、通信软件、游戏和下载任务的用户。

当前版本为 **0.1.4**，定位为首个可用版，重点覆盖窗口发现、真实动态预览以及一次拖拽完成跨桌面和跨显示器移动。Workspace 定义、应用启动计划和 PowerToys 导入将在后续版本加入。英文变更记录见 [0.1.4 发布说明](docs/RELEASE_NOTES-0.1.4.md)。

## 功能

- Windows 11 Fluent UI、Mica 背景、圆角卡片、主题适配和高 DPI 布局。
- 顶部桌面导航区区分“正在查看”和系统当前所在的桌面。
- 按物理显示器分组展示窗口，并根据可用空间自动调整卡片列数。
- 普通顶层窗口使用 DWM 动态缩略图；最小化窗口尽力保留系统缓存画面。
- 将窗口拖到桌面卡片可改变虚拟桌面归属；拖到显示器区域（包括空白区域）可同时改变桌面归属和显示器位置。悬停桌面卡片 400 毫秒可展开跨屏目标。
- 右键菜单提供与拖拽相同的移动操作。
- 鼠标悬停窗口标题栏时显示关闭按钮。按钮发送 `WM_CLOSE`，不会强制结束进程，应用仍可保存、取消或转为最小化到托盘。
- 仅在需要用户处理的错误时显示提示栏；成功、普通信息和警告不会留下提示横幅。
- 支持混合 DPI、负坐标、竖屏、任务栏、最大化和最小化窗口。
- 单实例运行、托盘常驻，可配置全局快捷键（默认 `Win + ``）。
- 设置中可在简体中文和 English 之间切换，切换后立即更新界面和托盘菜单。
- 使用普通用户权限运行，不自动提权，也不记录窗口画面。

## 系统要求

已验证目标环境：

- Windows 11 25H2 x64
- 系统构建 `26200.6899`
- .NET SDK `10.0.300`（或同一功能带内更高的服务版本）
- Windows SDK `10.0.26100`
- 带桌面 C# 工作负载的 Visual Studio Build Tools / MSBuild

虚拟桌面适配器仅在已验证的系统构建上启用。遇到未知构建时，程序保留窗口和显示器功能，并显示兼容性错误，不会调用未经验证的私有接口。

## 下载和运行

便携版发布包包含 .NET 运行时、Windows App SDK、编译后的 XAML 资源、原生适配器、许可证和校验文件。请解压完整目录后运行：

```powershell
.\WorkspaceManager.App.exe
```

不要只复制 exe 文件；完整的 `win-x64` 目录才是可运行的发布单元。

程序会在鼠标所在显示器的工作区域打开。关闭面板或按 `Esc` 只会隐藏面板，托盘进程仍会保留；再次运行 exe 会呼出已有实例。

## 操作方式

顶部桌面卡片决定当前查看内容。高亮卡片是正在查看的桌面，带有“当前所在”标记的卡片是系统当前桌面。“进入桌面”按钮会切换系统桌面并收起管理器。

窗口卡片支持：

1. 单击卡片：切换到所属桌面、激活窗口并收起管理器。
2. 将鼠标移到标题行：显示右上角关闭按钮。
3. 拖到桌面卡片：只改变虚拟桌面归属。
4. 拖到显示器面板或其空白区域：同时改变虚拟桌面归属和显示器位置。
5. 右键窗口：使用“打开窗口”和“移动到”命令。
6. 按 `Esc`：取消拖拽或收起面板。

移动完成后管理器保持打开，不会自动跟随窗口。程序会根据目标显示器工作区计算位置，在 DPI 不同、坐标为负或窗口过大时保持逻辑尺寸并限制到可见区域。Windows 允许时，最大化和最小化状态会一并恢复。

关闭按钮发送请求前会重新校验窗口句柄、进程 ID 和进程创建时间。源窗口消失后，下一次刷新会释放对应卡片和原生预览宿主，同时保留其他窗口的预览。

## 语言和设置

打开“设置”，可以配置：

- 呼出快捷键的修饰键和主键。
- 跟随系统、浅色或深色主题。
- `简体中文` 或 `English` 界面语言。

语言设置保存在 `%LOCALAPPDATA%/DesktopWorkspaceManager/settings.json`，重新启动后仍然有效。快捷键被其他程序占用时，原设置会保留，托盘入口仍可使用。

## 构建

在 PowerShell 中恢复、编译并运行：

```powershell
.\scripts\Build.ps1
.\scripts\Run.ps1
```

项目分为四个运行时层和一个测试项目：

```text
src/WorkspaceManager.Core              模型、契约、位置计算和移动协调
src/WorkspaceManager.Windows           Win32、DWM、显示器、虚拟桌面适配器和托盘
src/WorkspaceManager.App               WinUI 3 界面和交互
tests/WorkspaceManager.Tests           确定性单元测试
tools/WorkspaceManager.Diagnostics     隔离的 Windows 集成测试窗口
```

UI 通过服务接口工作，不直接调用 Win32。窗口身份同时包含 HWND、PID 和进程创建时间，用于防止句柄复用；Explorer 重启后会重建适配连接。

## 验证和发布

运行确定性测试：

```powershell
.\scripts\Test.ps1
```

可选的专项验证：

```powershell
.\scripts\Test.ps1 -Integration       # 桌面和显示器移动
.\scripts\Test.ps1 -Preview           # DWM 注册、裁剪和滚轮转发
.\scripts\Test.ps1 -Close             # WM_CLOSE 行为和过期身份
.\scripts\Test.ps1 -UiSmoke           # 隐藏 WinUI/资源生命周期
.\scripts\Test.ps1 -UiInteraction     # 可见拖拽、菜单和对话框回归
.\scripts\Test.ps1 -UiClose           # 管理器关闭按钮生命周期
```

生成自包含发布目录和 ZIP：

```powershell
.\scripts\Publish.ps1 -Zip
```

发布脚本会运行实际发布程序的冒烟测试，复制文档和第三方声明，并生成 `SHA256SUMS.txt`。

## 数据和限制

设置和轮换日志保存在：

```text
%LOCALAPPDATA%/DesktopWorkspaceManager/
```

日志包含操作类型、结果、异常类型、HRESULT 和堆栈信息，不包含窗口标题、命令行或预览像素。程序不上传遥测数据或窗口内容。

已知限制：

- 私有虚拟桌面接口受系统构建约束，Windows 更新可能改变其行为。
- 固定到所有桌面的窗口可以跨显示器移动，但首版不会通过拖拽修改其固定状态。
- 受保护、权限受限、从未绘制或最小化的窗口可能无法提供有用画面，此时会显示图标、标题和状态。
- Windows 可能因完整性级别不同而拒绝激活、关闭或调整窗口位置，程序会报告错误但不会自动提权。
- Workspace 持久化和启动计划、PowerToys 导入、每个显示器独立虚拟桌面、Shell 注入、资源监控、Win+Tab 替换、安装器和自动更新不在本版本范围内。

## 第三方组件

虚拟桌面访问使用固定版本的 x64 [VirtualDesktopAccessor](https://github.com/Ciantic/VirtualDesktopAccessor)。二进制、校验值、README 和许可证位于 `third_party/VirtualDesktopAccessor`，发布包的 `licenses` 目录也会包含相应文件。其他包的声明会在发布时收集。

## 开源协议

本项目使用 [MIT License](LICENSE)。第三方组件继续遵循各自的许可证，详见 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) 及便携版中的声明文件。
