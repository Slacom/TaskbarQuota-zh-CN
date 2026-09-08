<p align="center">
  <img width="128" height="128" alt="TaskbarQuota" src="src/taskbarquota.png" />
</p>

<h1 align="center">TaskbarQuota-zh-CN</h1>

<p align="center">
  Windows 任务栏中的 AI 服务用量、成本和智能体活动小组件。
</p>

<p align="center">
  <a href="https://apps.microsoft.com/detail/9n3kl49vfpvn?hl=zh-CN&amp;gl=CN&amp;mode=direct">
    <img src="https://get.microsoft.com/images/zh-cn%20dark.svg" width="200" alt="从 Microsoft Store 获取 TaskbarQuota" />
  </a>
  <a href="https://github.com/Slacom/TaskbarQuota-zh-CN/releases/latest">
    <img src="https://img.shields.io/badge/从-GitHub%20Releases-24292f?logo=github&amp;logoColor=white&amp;labelColor=57606a" height="32" alt="从 GitHub Releases 下载" />
  </a>
</p>

TaskbarQuota 是一款原生 Windows 应用，适合同时使用多个 AI 编程工具的用户。它会识别当前聚焦的应用或终端，并将对应的用量显示在系统托盘附近；打开主窗口后，还可以查看用量历史、模型成本和本地智能体活动。

所有处理都在本机完成。TaskbarQuota 没有账号系统、云端后端，也不会向 TaskbarQuota 服务器发送遥测数据。

## 安装

支持 Windows 10 版本 2004（内部版本 19041）及更高版本，推荐使用 Windows 11。

- 可从 [Microsoft Store](https://apps.microsoft.com/detail/9n3kl49vfpvn?hl=zh-CN&gl=CN&mode=direct) 安装官方版本。
- 也可以从 [GitHub Releases](https://github.com/Slacom/TaskbarQuota-zh-CN/releases/latest) 下载 x64 或 arm64 安装包。
- `TaskbarQuota-1.3.2.3-x64-zh-CN-portable.zip` 是无需安装的便携版本。

GitHub 安装包目前未签名，Windows SmartScreen 可能会要求确认。请先核对发布页中的文件名和 SHA-256，再根据需要选择“更多信息”→“仍要运行”。

## 功能

### 任务栏用量小组件

小组件会放在通知区域附近，可拖动到合适的位置，并根据服务显示额度窗口、百分比、重置时间、余额或积分。

- 自动跟随当前使用的 AI 工具；
- 最多固定三个服务；
- 支持多显示器任务栏；
- 可改为始终置顶的悬浮小组件；
- 支持仅显示进度条、仅显示百分比，或同时显示两者；
- 百分比可显示已用额度或剩余额度。

点击小组件可打开快速面板，打开主窗口可以查看所有服务并修改设置。

### Codex 额度显示边界

只有从 OpenAI 成功取得并确认的实时额度才会作为当前额度显示。如果网络中断、VPN 关闭或 OpenAI 服务暂时无法访问，任务栏不会继续显示上一次的订阅额度，而是显示：

```text
5小时额度 --
每周额度 --
```

这样可以明确区分“真实额度”和“当前无法确认”。恢复网络并成功刷新后，真实额度会重新显示。

### 自动识别工具

聚焦 AI 桌面应用时，TaskbarQuota 会根据进程匹配服务；聚焦终端时，会识别 Codex、Claude Code、OpenCode、Cline、Kimi、Grok 和 GitHub Copilot 等 CLI 智能体。

在 OpenCode、Cline、Synara 和 T3 Code 等支持的宿主中切换服务或模型时，小组件也会跟随更新。

### 智能体活动

独立的活动小组件会显示本地编程智能体的工作状态，例如工作中、等待、空闲、已完成或失败；快速面板可以跳转到活动会话。

活动信息来自本地进程和会话数据，支持 Codex、Claude、OpenCode、Cline、Kimi、Grok、Antigravity、GitHub Copilot 和 ZCode。已完成的活动会自动过期，也可以单独隐藏活动小组件或完全关闭本地监控。

### 成本和用量历史

“成本”页面会汇总本机能够取得的服务数据，包括：

- 今天、最近 7 天和最近 30 天的成本与 Token 总量；
- 每日历史和服务对比；
- 按模型拆分的 Token 和成本；
- 服务直接报告的成本，以及基于内置价格数据计算的估算值；
- 可分享的汇总卡片。

不同服务保存的数据不同。估算值会明确标注；缺少足够数据时不会编造成本。

### 仪表板、通知和设置

仪表板会显示已启用服务的计划、额度窗口、重置时间、余额和历史信息。应用可以随 Windows 启动，并在额度达到阈值或 Codex 重置额度即将过期时发送 Windows 通知。

“隐藏不可用服务”只自动处理最近一次取数明确报告为“尚未安装”的服务。需要登录或等待桌面应用启动的服务会保留，方便用户修复凭据；开关关闭后，由该策略自动隐藏的服务会恢复显示。用户手动隐藏、禁用或固定的设置优先级更高，自动隐藏不会清除固定偏好。

当任务栏可用空间暂时不足时，小组件会在内存中暂时隐藏放不下的非活动服务，空间恢复后再显示；不会把用户的固定设置改成“取消固定”。

额度补充通知默认开启，并且独立于警告、严重阈值通知。它会在基于百分比的窗口可用额度至少增加 **10 个百分点** 时通知，包括部分补充，以及重置后已经使用一部分额度、当前读数达到 99% 的情况。主窗口、次窗口、模型窗口、月度窗口和额外窗口分别追踪，同一服务的多个窗口会合并为一条通知。

应用启动或重新启用补充通知后的第一次实时读数只建立基线，不会发送通知。另有一个默认关闭的“跨会话变化”选项，可以比较启动后的第一次实时读数与上一次确认的实时观察值。

## 支持的服务

| 服务 | 显示内容 | 自动凭据来源 |
| --- | --- | --- |
| Codex | 5 小时、每周额度和重置额度 | Codex OAuth 会话 |
| GitHub Copilot | 聊天和代码补全额度 | 环境变量、已保存 Token 或 GitHub CLI |
| Claude | 5 小时、每周和模型窗口 | Claude OAuth 会话 |
| Antigravity | 本地额度状态 | 正在运行的语言服务器 |
| Cursor | 计划用量和限制 | 本地应用数据或浏览器会话 |
| OpenCode Zen | 成本和余额 | 浏览器会话或手动 Cookie |
| OpenCode Go | 滚动、每周和月度窗口 | 浏览器会话或本地数据 |
| Cline Usage-Billing | 额度余额 | 本地 Cline 账户会话 |
| ClinePass | 5 小时、每周和月度窗口 | 本地 Cline 账户会话 |
| Z.ai | 5 小时、每周和 MCP 窗口 | ZCode 配置或 API 密钥 |
| Kimi | 5 小时和每周额度 | Kimi Code OAuth 或 API 密钥 |
| Grok | 积分和月度窗口 | Grok CLI 会话 |
| Devin | 每日、每周和额外用量 | Devin CLI 或桌面会话 |

应用会尽量复用各服务已经保存的凭据。如果自动识别失败，可以在服务卡片上点击“修复”，手动输入 Token 或 Cookie。

## 隐私

- 用量请求从本机直接发送到对应服务；
- 智能体活动和由提示词生成的会话标题只保留在本机；
- 不收集遥测，也不向 TaskbarQuota 服务器发送数据；
- 诊断日志只写入 `%TEMP%\taskbarquota.log`；
- 自动读取的浏览器 Cookie 只在内存中处理；
- 手动输入的凭据保存在 `%LOCALAPPDATA%\TaskbarQuota\credentials.json`，该文件应当保持私密；
- 跨会话额度补充状态保存在本地，其中包含窗口元数据、时间戳和单向身份哈希，不保存明文账号标识。

在活动面板中关闭“监控”后，会停止本地进程和会话检查，并清除内存中保留的活动信息。

## OpenCode 登录说明

现代 Chromium 浏览器使用 App-Bound Encryption 保护 Cookie，TaskbarQuota 可能无法读取新版 Chrome、Edge 或 Brave 中的 OpenCode 会话。支持基于 Firefox 的浏览器；也可以在服务卡片的“修复”对话框中粘贴 OpenCode 请求，支持完整 cURL 命令或 `Cookie` 请求头。

Cookie 请求头属于会话凭据，请不要粘贴到 Issue 或公开聊天中。

## 从源码构建

需要：

- Windows 10，内部版本 19041 或更高；
- [.NET SDK 10](https://dotnet.microsoft.com/download)；
- Windows App SDK 和 WinUI 3 构建工具。

```powershell
# 构建
dotnet build src/TaskbarQuota.App/TaskbarQuota.App.csproj -c Debug -p:Platform=x64

# 运行
dotnet run --project src/TaskbarQuota.App/TaskbarQuota.App.csproj

# 测试
dotnet test tests/TaskbarQuota.Tests/TaskbarQuota.Tests.csproj
```

应用使用自包含的 WinUI 3 构建，不需要单独部署后端。

## 许可证

TaskbarQuota 使用 [MIT License](LICENSE) 发布。

## 本次中文分支修改（1.3.2.3）

本分支基于上游 [TaskbarQuota](https://github.com/zioder/TaskbarQuota)，并发布到 [Slacom/TaskbarQuota-zh-CN](https://github.com/Slacom/TaskbarQuota-zh-CN)：

- 完成主要界面、动态状态、服务名称、额度标签和通知文案的简体中文本地化；
- 使用独立的中文安装器名称、AppId 和默认安装目录 `TaskbarQuota-zh-CN`，避免与英文版互相覆盖；
- 修复 OpenAI/Codex 网络不可用时错误显示过期订阅额度的问题，改为两行中性 `--` 占位；本地会话历史也不会伪装成实时额度；
- 修复任务栏空间重新测量或刷新时自动取消用户固定的问题，固定偏好会保留；
- 隔离托盘上下文菜单的线程和异常路径，降低非打包 WinUI 应用因 H.NotifyIcon 菜单导致崩溃的风险；
- 接通服务发现取数结果，完善“隐藏不可用服务”的即时、可逆行为；
- 生成 x64 自包含便携包和安装包，便携包不包含用户凭据文件。

如果你发现服务识别、额度解析或本地化问题，提交 Issue 时请隐藏 Token、Cookie、auth.json 和其他凭据内容。
