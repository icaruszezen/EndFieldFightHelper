# EndFieldFightHelper

明日方舟：终末地 战斗辅助工具。基于 YOLO 目标检测与屏幕捕获实现自动攻击、闪避、技能释放等功能。

## 技术栈

- **语言** — C#（.NET 10）
- **UI 框架** — [Avalonia](https://avaloniaui.net/) 11 + [SukiUI](https://github.com/kikipoulet/SukiUI) 6
- **MVVM** — CommunityToolkit.Mvvm
- **目标检测** — YoloSharp + ONNX Runtime（DirectML 加速）
- **屏幕捕获** — DXGI / GDI+ / BitBlt / PrintWindow 多种实现

## 系统要求

| 项目 | 最低要求 |
|------|---------|
| 操作系统 | Windows 10 1903（build 18362）及以上 |
| 架构 | x64 |
| .NET SDK | [.NET 10](https://dotnet.microsoft.com/download/dotnet/10.0)（需包含 `10.0.22621.0` Windows SDK） |
| 权限 | 管理员权限（应用清单已声明 `requireAdministrator`） |

## 获取源码

```bash
git clone https://github.com/<your-org>/EndFieldFightHelper.git
cd EndFieldFightHelper
```

## 下载游戏资源

构建前需先拉取 [Endaxis](https://github.com/Lieyuan621/Endaxis) 提供的公共资源（角色头像、图标、gamedata 等）。项目已提供自动化脚本：

```powershell
.\scripts\download-endaxis-public.ps1
```

脚本会从 GitHub 下载 Endaxis 仓库的 `public/` 目录，并同步到 `Resources/public/`。如需指定其他目标路径：

```powershell
.\scripts\download-endaxis-public.ps1 -TargetDir "C:\custom\path"
```

> 如果网络不畅，也可手动下载 https://github.com/Lieyuan621/Endaxis 仓库，将其中的 `public/` 文件夹复制到 `Resources/public/`。

## 构建

### 命令行

```bash
# Debug 构建
dotnet build

# Release 构建
dotnet build -c Release
```

### VS Code

项目已配置 `.vscode/tasks.json` 和 `.vscode/launch.json`，可直接使用：

1. 打开项目根目录
2. 按 `Ctrl+Shift+B` 执行默认构建任务（Debug）
3. 按 `F5` 启动调试

可选任务列表：

| 任务名 | 说明 |
|--------|------|
| `build` | Debug 构建 |
| `build-release` | Release 构建 |
| `publish` | 发布 |
| `watch` | 热重载运行（`dotnet watch`） |

### Visual Studio

使用 Visual Studio 2022 17.14+ 打开 `EndFieldFightHelper.slnx`，选择对应配置后构建即可。

> `.slnx` 是 SDK 风格的解决方案文件，需要较新版本的 Visual Studio 支持。

## 发布

```bash
dotnet publish -c Release
```

输出位于 `bin\Release\net10.0-windows10.0.22621.0\win-x64\publish\`。

## 运行

应用要求**管理员权限**运行（用于屏幕捕获和模拟输入）。直接执行构建产物中的 `EndFieldFightHelper.exe` 即可。

## 项目结构

```
EndFieldFightHelper/
├── Views/              # Avalonia 页面视图（.axaml）
├── ViewModels/         # MVVM 视图模型
├── Services/           # 业务服务（截图、热键、YOLO 检测、自动战斗等）
│   └── Capture/        # 屏幕捕获实现（DXGI、GDI+、BitBlt、PrintWindow）
├── Models/             # 数据模型
├── Converters/         # XAML 值转换器
├── Helpers/            # 工具类（Win32 互操作等）
├── Resources/
│   └── public/         # 游戏资源（由脚本下载，不纳入版本控制）
├── scripts/            # 自动化脚本
└── EndFieldFightHelper.csproj
```
