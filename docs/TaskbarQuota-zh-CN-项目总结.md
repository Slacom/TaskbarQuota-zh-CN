# TaskbarQuota-zh-CN 跨对话交接文档

> 用途：在不同 Codex 对话之间交接本项目的代码、汉化、功能修正、发布和排障上下文。本文记录可复核的项目状态，不包含任何用户凭据、Cookie、Token 或本机运行数据。

## 当前基线

| 项目 | 状态 |
| --- | --- |
| 上游仓库 | [zioder/TaskbarQuota](https://github.com/zioder/TaskbarQuota) |
| 中文仓库 | [Slacom/TaskbarQuota-zh-CN](https://github.com/Slacom/TaskbarQuota-zh-CN) |
| 当前分支 | `localization/zh-CN` |
| 当前版本 | `1.3.2.2` |
| 目标平台 | Windows x64；WinUI 3；.NET 10；自包含发布 |
| 发布形式 | 中文安装器、x64 便携 ZIP |
| 许可证 | MIT |

版本号需要同时保持以下位置一致：应用项目文件、`Package.appxmanifest`、`installer/TaskbarQuota.iss`、README 中的发布说明，以及最终产物文件名。发布前应以源码中的实际版本和构建产物元数据为准，不要只依据文件名判断版本。

## 当前交接快照（2026-09-11）

| 项目 | 当前事实 |
| --- | --- |
| 本地仓库 | `E:\Codex_Workspace\额度显示窗口\TaskbarQuota-zh-CN` |
| 当前分支 | `localization/zh-CN` |
| 当前 HEAD | `bf1a5e1d57885e75d5fc29665a3727ad44065903`（`docs: refine README scope`） |
| 本地工作树 | 已检查，当前无未提交改动 |
| 中文 Fork 远程 | `fork` → `https://github.com/Slacom/TaskbarQuota-zh-CN.git` |
| 上游远程 | `origin` → `https://github.com/zioder/TaskbarQuota.git` |
| Fork 分支最后一次远程确认 | 2026-09-09，远程 `localization/zh-CN` 回读为 `bf1a5e1d57885e75d5fc29665a3727ad44065903` |
| 2026-09-11 远程复核 | 未完成：通过 `127.0.0.1:10910` 访问 GitHub 443 端口失败；不要据此断言远程今天没有其他变化 |
| 源码与安装器版本 | `1.3.2.2` |

## 已完成工作范围

- 完成主要 WinUI 页面、任务栏/浮动小组件、托盘菜单、服务卡片、设置、通知、智能体活动和凭据/登录提示的简体中文本地化；
- 增加动态中文格式化，包括更新时间、重置时间、Token 数量、数据来源、状态和额度提示；
- 针对 Codex 增加实时额度边界：只有确认的实时读数才能显示；网络失败、请求失败或仅有本地历史时显示 `5小时额度 --` 和 `每周额度 --`，不显示旧额度；
- 调整服务发现和“隐藏不可用服务”策略：新用户默认显示全部服务，只自动隐藏明确报告为“尚未安装”的服务，且策略可恢复；
- 同步供应商导航、仪表板开关、服务卡片和托盘入口，避免无效服务回退到 Codex 或意外重新启用；
- 任务栏空间不足、重新测量或布局变化时只临时隐藏非活动小组件，不改写用户固定偏好；
- 隔离托盘菜单线程和异常路径，修正托盘图标创建时机并保留 EXE 图标回退；
- 使用独立中文 AppId、名称和默认安装目录 `TaskbarQuota-zh-CN`，提供 x64 自包含安装包和便携包；
- 卸载流程增加“保留配置”选择，并处理当前配置、旧版配置和静默卸载路径；
- README 已精简为四个部分：说明、汉化范围、功能调整、针对 Codex 额度的显示修改。Fork 说明和上游链接位于 README 开头。

## 项目定位

TaskbarQuota 是 Windows 任务栏额度监控工具。它从多个本地应用、CLI 会话、浏览器会话或服务接口读取用量信息，在任务栏小组件、仪表板、浮动窗口、托盘菜单和成本页面中展示。中文分支的主要目标是：

- 提供简体中文界面、服务名称、状态标签、通知文案和设置说明；
- 保持上游多服务额度监控能力，同时避免中文安装与英文安装互相覆盖；
- 在网络不可用、数据不可确认或首次运行时明确显示中性占位，而不是把旧数据伪装成实时额度；
- 让服务可见性、仪表板入口、固定偏好和任务栏空间管理保持一致；
- 保留本机优先和凭据不出本机的使用边界。

## 主要架构

### 应用层

- `src/TaskbarQuota.App/App.xaml.cs`：应用启动、单实例和初始化流程。
- `MainWindow`、`DashboardPage`、`SettingsPage`、`CostPage`：主窗口页面与设置入口。
- `DashboardViewModel`、`ProviderCardViewModel`、`SettingsViewModel`：页面状态和服务卡片状态。
- `DashboardNavigationBinder`：将服务发现结果、仪表板开关和可用卡片绑定到导航入口。

### 用量与服务层

- `src/TaskbarQuota.App/Usage/UsageService.cs`：统一调度服务读取、缓存、失败回退和结果状态。
- `UsageCoordinator.cs`：协调刷新、发布观察结果、活动状态和相关 UI 更新。
- `Usage/Providers/`：各服务适配器，例如 Codex、Claude、Copilot、Cline、Kimi、Grok、Devin、Z.ai、OpenCode 和 Antigravity。
- `UsageSnapshotStore.cs`、`UsageHistoryService.cs`：本机快照与历史数据。
- `CredentialStore.cs`：本机凭据存储；严禁把其输出或测试运行数据加入 Git。

Codex 离线显示约定：当结果来自网络失败回退、本地会话历史回退，或没有可确认的实时额度时，任务栏小组件仍保留 `5小时额度` 与 `每周额度` 两个标签，但数值显示为 `--`。这表示“当前无法确认”，不是额度为零，也不是最后一次成功读数。

### 任务栏与托盘层

- `Taskbar/TaskBarManager.cs`：任务栏目标、窗口生命周期、刷新和布局协调。
- `Taskbar/TaskBarWidget.cs`：服务磁贴、活动磁贴和空间不足时的内存级隐藏。
- `Taskbar/TaskbarContentRouter.cs`：内容路由。
- `Taskbar/PinBudgetService.cs`：固定容量判断；放不下的磁贴不应改写用户的固定偏好。
- `TrayIconContextMenuTests.cs` 对托盘菜单生命周期和线程边界提供回归覆盖。托盘图标句柄在创建前准备，并保留 EXE 图标回退路径。

### 配置与服务发现

- `Services/WidgetSettingsService.cs`：供应商显示、固定、行显示和自动隐藏策略。
- `Services/ProviderDiscoveryService.cs`：服务安装状态、取数结果和自动隐藏状态。
- 首次运行且缺少自动隐藏配置时，默认关闭“隐藏不可用服务”，因此新用户会看到全部支持的服务；现有配置文件中的 `0/1` 仍然优先。
- 自动隐藏只针对最近一次取数明确报告“尚未安装”的服务。需要登录、凭据失效或网络暂时不可用的服务不会被误判为未安装。
- 用户手动隐藏、禁用或固定的偏好优先于自动策略；关闭自动隐藏后，被该策略隐藏的服务可恢复显示。

## 本地数据与隐私边界

默认运行数据位于 `%LOCALAPPDATA%\TaskbarQuota`，诊断日志位于 `%TEMP%\taskbarquota.log`。其中可能包含凭据、运行状态、缓存、快照或服务配置，均不属于源码发布内容。

发布前必须确认：

1. `git status` 中没有用户数据或临时文件；
2. 便携包只来自 `dotnet publish` 输出，不从 `%LOCALAPPDATA%`、`%TEMP%` 或已安装目录复制文件；
3. ZIP 和安装器内容中没有 `credentials.json`、Cookie、Token、`auth.json`、日志、转储、SQLite 用户数据库等个人运行文件；
4. 检查服务适配器时不把真实账号标识、请求头或完整 Cookie 写进测试、日志或提交信息。

安装器使用独立的中文 AppId、默认目录 `TaskbarQuota-zh-CN` 和中文显示名称，避免覆盖英文版。卸载界面提供一个“保留配置”选项；默认保留配置，取消后才清理当前及旧版用户配置目录。非交互静默卸载默认保留配置。

## 构建与测试

源码构建：

```powershell
dotnet build src/TaskbarQuota.App/TaskbarQuota.App.csproj -c Debug -p:Platform=x64
dotnet test tests/TaskbarQuota.Tests/TaskbarQuota.Tests.csproj
```

发布时使用 x64、Release、自包含参数，并将输出放到版本化的 `artifacts/TaskbarQuota-zh-CN-<version>-x64` 目录。之后生成：

- `TaskbarQuota-<version>-x64-zh-CN-portable.zip`；
- `TaskbarQuotaSetup-<version>-x64-zh-CN.exe`。

当前 1.3.2.2 发布前已完成的自动化基线为：`.NET` 测试 `891/891` 通过。重点回归范围包括：

- Codex 离线/本地历史中性占位；
- 供应商导航与仪表板可见性；
- 新用户自动隐藏默认值；
- 固定预算不会清除用户固定偏好；
- 托盘菜单生命周期和布局周期；
- 本地化文本与资源；
- 各服务适配器、凭据存储、历史记录和通知。

自动化通过不等于所有原生桌面交互都已验证。托盘右键、Shell 通知区域图标和特定 DPI/桌面环境应在真实 Windows 会话中抽查；如果测试环境无法提供原生托盘 UI，应在发布记录中明确标注为“未验证”。

### 验证状态解释

- `891/891` 是已有项目记录中的 1.3.2.2 发布前历史测试基线，不是本次 2026-09-11 交接时重新运行的结果；后续代码变更或发布前应重新执行完整测试。
- 已有重点回归覆盖 Codex 离线占位、服务可见性、导航同步、固定预算、托盘菜单、布局周期和本地化文本；覆盖情况以当前测试文件和最新测试结果为准。
- 真实 Windows 托盘、通知区域、DPI、多显示器、安装升级和卸载交互仍需独立验收，不能仅凭单元测试通过宣称完成。

## 发布流程

1. 确认版本、工作树和远程分支；只允许推送到中文 Fork，不推送上游仓库。
2. 运行完整测试，并记录通过数。
3. 清理旧生成物时保留用户明确要求保留的历史版本；不要删除用户数据或英文安装目录。
4. 重新 `dotnet publish`，检查文件数量、x64 PE 标识、应用图标、WinUI 资源和语言资源。
5. 生成 ZIP 与 Inno Setup 安装器，记录大小、SHA-256、产品名、产品版本和签名状态。
6. 对安装器执行隔离目录安装、升级和卸载检查；测试目录必须是专用临时目录，不能指向用户现有安装。
7. 生成中文项目总结或更新本文件后再提交源码，确保文档中的版本与最终提交一致。
8. 推送 `localization/zh-CN`，创建对应的中文 Release，并回读分支 SHA、Tag 目标 SHA、Release 状态和资产摘要。

Release 说明应只描述相对于上游的用户可见功能差异，例如中文界面、独立安装身份、额度不可确认时的中性显示、服务可见性策略和任务栏固定行为。不要在 Release 说明中写“修复 Bug”或未经验证的桌面交互结论；详细排障背景保留在源码文档或维护记录中。

## GitHub Fork 展示与发布状态

- README 的两次本地提交为 `b1c6ff0` 和 `bf1a5e1`，最终提交已通过 VPN 代理 `127.0.0.1:10910` 推送到 `fork/localization/zh-CN`；最后一次远程 SHA 回读记录见“当前交接快照”。
- GitHub 仓库根地址当前截图显示默认分支仍为 `main`，而中文内容在 `localization/zh-CN`，因此直接进入根地址可能看到上游 `main` 的内容。
- 若希望进入 `https://github.com/Slacom/TaskbarQuota-zh-CN` 后直接看到中文内容，应在 GitHub 仓库设置中将默认分支改为 `localization/zh-CN`：`Settings → General → Default branch`。
- `forked from zioder/TaskbarQuota` 是 GitHub 的正常 Fork 关系标识；设置默认分支不会移除它，也不需要把仓库变成独立仓库。
- 本次交接中没有修改 GitHub 默认分支、没有创建新的 GitHub Release，也没有将任何提交推送到 `origin` 上游仓库。

使用 VPN 推送时只在命令级设置代理，不改变全局 Git 配置：

```powershell
git -c http.proxy=http://127.0.0.1:10910 `
    -c https.proxy=http://127.0.0.1:10910 `
    push fork localization/zh-CN
```

推送后应使用同一代理回读远程 SHA：

```powershell
git -c http.proxy=http://127.0.0.1:10910 `
    -c https.proxy=http://127.0.0.1:10910 `
    ls-remote fork refs/heads/localization/zh-CN
```

## 后续对话的快速入口

- 先检查 `PROJECT_PROGRESS.md`、`latest_run.json` 的习惯不适用于本仓库；本项目应优先查看 `README.md`、本文件、`git status`、当前版本号和 `artifacts/`。
- 讨论“额度不更新”时，先区分实时成功、内存缓存、失败回退、本地历史回退和首次运行无数据。
- 讨论“服务消失”时，先检查 `%LOCALAPPDATA%\TaskbarQuota` 的显式设置，再检查自动隐藏开关、服务发现结果和导航绑定。
- 讨论“固定消失”时，检查 `PinBudgetService` 是否只影响当前布局，不能把空间不足当成用户取消固定。
- 讨论安装/卸载时，先确认是否有正在运行的 `TaskbarQuota.exe`，并避免触碰用户现有英文安装和本机配置。
- 所有发布结论都要区分“源码已实现”“自动化已验证”“真实桌面交互已验证”和“尚未验证”。
- 讨论 GitHub 页面显示内容时，先确认当前打开的分支和仓库默认分支，不要把 `forked from` 标识误判为 README 或代码没有上传。
- 讨论远程同步时，先检查本地 HEAD、跟踪分支和远程 SHA；若网络或代理失败，只记录为“远程状态未复核”，不要把历史回读结果冒充当前结果。
