# ADR 0001 Windows 宿主与本地界面

日期：2026-11-09。状态：采用；实现和 Windows 验证持续进行。

## 背景

牛马不放器已有可点击的 HTML/CSS/JavaScript 界面，包含计薪、休息、情绪短句与拖拽揉纸。用户要求将它做成规范的 Windows 软件并在 GitHub 开源。

Windows 系统集成需要原生能力；直接重复实现整套交互会增加首版成本。旧有 .NET 8 工具能说明计薪与桌面入口的使用方式，但不应把机器私有路径、用户配置或 Explorer 内部注入方式直接带进新仓库。

## 决策

采用 **.NET 10 LTS + WPF 宿主 + WebView2 本地 UI + 纯 C# Core**。

- Core 保存工资校验、计薪和配置存储规则，不依赖 UI 框架。
- WPF 管理小挂件、面板、托盘、自启动和生命周期。
- WebView2 加载打包的本地资产，保留已有交互和原创图形。
- 生产模式使用真实系统时间和宿主状态；浏览器演示保留虚构场景并与真实配置分离。
- 小挂件位于工作区底部，不嵌入 Explorer 任务栏。
- 首版发布自包含 win-x64 ZIP，WebView2 Evergreen Runtime 为单独依赖。

## 依据

.NET 8 的官方支持将于 2026-11-10 结束，.NET 10 LTS 支持至 2028-11-14，因此新项目基线采用 .NET 10。WPF 已提供正式 .NET 10 支持。来源：[.NET 支持策略](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core)、[WPF for .NET 10](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/whats-new/net100)。

WebView2 虚拟主机映射可让本地界面获得固定 HTTPS origin 和相对资源路径，不必运行 HTTP 服务。仅映射发布资产，并按最小跨来源需求使用 `Deny`。来源：[加载本地内容](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/working-with-local-content)、[虚拟主机映射 API](https://learn.microsoft.com/en-us/dotnet/api/microsoft.web.webview2.core.corewebview2.setvirtualhostnametofoldermapping)。

消息桥必须验证来源和输入，禁用不需要的通用宿主能力，限制导航与权限。来源：[WebView2 安全实践](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/security)。

Evergreen Runtime 可独立维护并共享；Windows 11 通常包含它，但应用仍需检测缺失情况。来源：[分发 WebView2 Runtime](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/distribution)。

## 考虑过的方案

**纯 WPF 重写界面**：减少 WebView2 依赖，但会重复实现已经打磨的纸张动画、拖拽和页面布局。暂不作为首版方案，若后续性能或可访问性实测证明存在明显问题再评估。

**Electron**：可复用界面，但首版只需要少量 Windows 集成；再次引入独立 Chromium/Node 发行和桥接体系不符合当前范围。

**Tauri**：可使用系统 WebView，但需要新增 Rust 工具链和一套 Windows 集成实现；现有 C# 能力与纯 Core 更适合当前维护起点。

**继续注入 Explorer 任务栏**：依赖系统实现细节，兼容性和恢复成本较高。首版用工作区小挂件与系统托盘提供相同入口目的，不把注入设为发布前提。

## 结果与约束

界面能从原型逐步进入原生宿主，Core 可独立测试，Windows 专属行为有明确边界。代价是同时维护 C# 和少量 JavaScript，且必须说明 WebView2 Runtime 前置依赖。

原生端是持久状态和真实计薪的权威来源。若演示模型与 Core 同时存在，必须通过边界用例验证一致，不允许网页自行保存真实设置后再补发宿主确认。

项目不新增后端、账户、分析埋点或云同步。配置只在当前用户目录保存；烦恼文字不持久化。发布脚本保留第三方许可，不把自包含 .NET 与自带 WebView2 混为一谈。

本决策不等于已经通过多 DPI、多显示器、全屏、任务栏自动隐藏或安全审计。相应结果必须单独记录。
