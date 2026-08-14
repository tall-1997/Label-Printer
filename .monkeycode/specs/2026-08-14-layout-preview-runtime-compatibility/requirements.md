# 布局与预览运行时兼容修复需求

## Introduction

本功能修复侧栏折叠占位、页面同行控件错位、订单管理页日志折叠空白和 BarTender 2022 R2 预览运行时不兼容问题。

## Glossary

- **折叠侧栏**：导航项隐藏且仅通过顶栏菜单按钮重新展开的侧栏状态。
- **同行控件**：在同一操作行内共同显示的标签、输入控件、复选框和按钮。
- **预览宿主**：承载 BarTender 2022 R2 .NET SDK 的独立 .NET Framework x64 进程。
- **主应用**：运行于 .NET 8 的 BarTenderPrinter WinForms 进程。

## Requirements

### Requirement 1: 折叠侧栏释放空间

**User Story:** AS 操作员, I want 折叠侧栏完全释放主界面宽度, so that 打印和订单区域获得完整显示空间。

#### Acceptance Criteria

1. WHEN 侧栏折叠, 主应用 SHALL 将侧栏面板宽度设置为零并隐藏侧栏面板。
2. WHEN 侧栏展开, 主应用 SHALL 显示导航项并重新计算打印页和订单页宽度。
3. WHEN 主窗口尺寸变化, 主应用 SHALL 重新计算顶栏、侧栏和页面内容边界。

### Requirement 2: 同行控件对齐

**User Story:** AS 操作员, I want 同行控件和文字保持一致的中心线, so that 页面结构清晰且操作目标易于识别。

#### Acceptance Criteria

1. WHEN 页面显示同行控件, 主应用 SHALL 按该行最大首选高度垂直居中各控件。
2. WHEN 历史工具栏换行, 主应用 SHALL 为按钮、标签、输入框和复选框应用一致的行内边距。
3. WHEN DPI 变化, 主应用 SHALL 使用当前 DPI 重新计算控件高度、间距和位置。
4. WHEN 订单编辑器宽度变化, 主应用 SHALL 重新计算订单筛选列宽及数据源表格边界。

### Requirement 3: 订单页回收日志空间

**User Story:** AS 订单管理员, I want 日志折叠后数据源表格扩展, so that 订单页底部不再显示无效空白。

#### Acceptance Criteria

1. WHEN 日志折叠, 主应用 SHALL 将订单页扩展至状态栏上方。
2. WHEN 订单内容区高度增加, 主应用 SHALL 将新增高度分配给数据源表格。
3. WHEN 订单内容区高度不足, 主应用 SHALL 保留最小表格高度并提供滚动区域。
4. WHEN 订单布局完成, 主应用 SHALL 将滚动最小高度设置为实际内容底边。

### Requirement 4: BarTender 预览运行时隔离

**User Story:** AS 操作员, I want 在 .NET 8 主应用中稳定开启 BarTender 2022 R2 预览, so that 预览不会因框架 API 差异失败。

#### Acceptance Criteria

1. WHEN 主应用检测 BarTender 2022 R2 SDK, 主应用 SHALL 通过 .NET Framework x64 预览宿主调用 SDK。
2. WHEN 预览宿主生成动态预览, 预览宿主 SHALL 打开模板、设置当前模板字段并导出 PNG。
3. WHEN 预览请求超过限定时间, 主应用 SHALL 终止本次预览宿主并显示明确错误。
4. IF 预览宿主、SDK 或 .NET Framework 运行环境不可用, 主应用 SHALL 保持打印功能可用并报告预览不可用原因。
5. WHEN 预览宿主退出, 主应用 SHALL 验证退出码和 PNG 文件有效性。
