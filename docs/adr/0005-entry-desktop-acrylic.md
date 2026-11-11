# ADR 0005 浮起入口使用桌面磨砂

日期：2026-11-11。状态：用户选择“桌面磨砂意向”并授权实现，0.1.7 本地开发预览。

## 背景

0.1.6 的联动浮起已在本机通过用户验收，但入口实色纸面显得突兀。本次将展开期的底部入口改为有绿色染色的真实背景 Acrylic；主面板与收起期的任务栏 child 保留现有显示方式。

参考 AF Media Bar 固定提交 `5e661f2d57ad8b44cc47bca665c9c8d4c22169ef` 的非激活背景适配思路。AF 媒体栏的模糊专辑封面模式与非激活窗口的 Accent 模式是两条路径，这里采用后者的原生材质路线，没有引入 AF 的运行时或复制其宿主。

## 决策与依据

- 本机 Windows 11 build 26300、150% DPI 的独立 WPF 探针：相同 owned、NoActivate、非 Layered 窗口中，DWM `TRANSIENTWINDOW` 请求返回成功但表现为实色；Accent Acrylic + 零 glass margin 可以模糊窗口后方的测试图案。Accent + 整窗 glass 在此宿主也为实色。该结果只代表这台机器，不能泛化到所有 Windows。
- 保留 `AllowsTransparency=false`、Topmost、owner、NoActivate、窗口原生位置与系统外框。`WindowChrome.GlassFrameThickness=0` 与 native 零扩框保持一致，避免后续 Chrome 更新覆盖材质。
- 将 `SetWindowCompositionAttribute/AccentPolicy` 封装在独立兼容适配器内。微软不推荐此 API，故不把调用接受当作跨系统视觉保证；API 失败时必须完整关闭材质并恢复实色。未来可替换该适配器而不改变入口协调。
- Accent 染色为 ABGR `0x85AECFBE`（RGB 190/207/174，alpha 133），独立 WPF 亮色层为 RGBA 247/250/242/71。金额、状态、时钟继续独立绘制，保持不透明、无模糊滤镜；不对整个窗口使用 Opacity，也不采集桌面图像。
- `EntryBackdropController` 在稳定环境中缓存结果；开窗或设置/主题/合成/电源变化时允许重新应用与重试，不在计薪更新中不断切换材质。注册节能状态电源通知，关闭时解除。
- 高对比度使用系统背景与前景；透明效果关闭、无合成、节能模式、非目标 Windows 或原生调用失败时使用同色可读实底。正常回退是完整表面，不能是全透明空卡片。
- 0.1.6 的显隐、失败回滚、槽位、右键和失焦保护不变。隐私变更的旧金额清除语义继续生效。

## 验证范围

纯 BCL 回归覆盖颜色通道、缓存、失败后的清理、恢复、零/替换句柄及 Release。链接真实 `EntryPopupWindow` 的原生 smoke harness 覆盖变化条纹、黑白背景、焦点不变、owner、NoActivate/Topmost/非 Layered、物理边界、设置通知重应用、十次开关与隐私遮罩。

系统全局透明/高对比度/节能开关不由测试自动更改，其分支以纯状态回归覆盖。不同 Windows build、多屏、其他 DPI 和远程会话仍需实机验证。系统模糊与投影不保证逐像素复现 HTML。

## 参考

- [AF 非激活窗口适配](https://github.com/Fervent-Tempo/AF-Media-Bar/blob/5e661f2d57ad8b44cc47bca665c9c8d4c22169ef/src/AFMediaBar/Classes/Services/Appearance/NativeWindowBackdropAdapter.cs#L82-L171)
- [DWM_SYSTEMBACKDROP_TYPE](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/ne-dwmapi-dwm_systembackdrop_type)
- [SetWindowCompositionAttribute](https://learn.microsoft.com/en-us/windows/win32/dwm/setwindowcompositionattribute)
