# 界面布局、可见性与预览优化需求

## Introduction

本功能统一修复打印页、订单管理页、侧边栏、表格、输入控件和预览窗口的布局与可见性问题，确保常用 DPI 和窗口尺寸下文字、图标、边框及选中状态清晰完整。

## Glossary

- **操作区**：主窗体内的打印页或订单管理页内容区域。
- **强调状态**：表格行、单元格、页签或导航项的选中状态。
- **预览窗**：显示 BarTender 标签图片的独立窗口。
- **同一基线**：同一水平行内控件的文字中心和控件中心保持一致。

## Requirements

### Requirement 1: 控件边缘与文字完整性

**User Story:** AS 操作员, I want 控件边缘和文字完整显示, so that 所有操作都可以准确识别。

#### Acceptance Criteria

1. WHEN 应用显示按钮、输入框、下拉框或数值框, 应用 SHALL 显示连续可辨认的控件边框。
2. WHEN 控件包含图标和文字, 应用 SHALL 将图标与文字作为整体在控件内水平和垂直居中。
3. WHEN 控件使用中文文字, 应用 SHALL 根据首选尺寸提供足够高度和内边距。
4. WHEN 同一行显示多个控件, 应用 SHALL 使控件中心线和文字中心线保持一致。

### Requirement 2: 选择状态与表格可读性

**User Story:** AS 操作员, I want 选中内容和数据源列清晰可辨, so that 当前选择和字段边界不会混淆。

#### Acceptance Criteria

1. WHEN 历史记录行被选中, 应用 SHALL 使用浅色强调背景和深色前景文字。
2. WHEN 订单数据源表格显示, 应用 SHALL 显示连续的水平及垂直单元格分隔线。
3. WHEN 订单数据源表格显示表头, 应用 SHALL 显示表头列分隔线和可见外边框。
4. WHEN 其他 DataGridView 显示选中状态, 应用 SHALL 使用与历史记录相同的可读对比规则。

### Requirement 3: 侧边栏与工作区

**User Story:** AS 操作员, I want 紧凑侧边栏和顶栏展开按钮, so that 主操作区获得更多显示面积。

#### Acceptance Criteria

1. WHEN 侧边栏收起, 应用 SHALL 使用紧凑导航宽度。
2. WHEN 操作员需要展开侧边栏, 应用 SHALL 在顶栏提供展开和收起按钮。
3. WHEN 侧边栏展开或收起, 应用 SHALL 重新计算打印页和订单管理页可用宽度。

### Requirement 4: 日志与订单管理布局

**User Story:** AS 订单管理员, I want 日志收起后订单页面填满工作区, so that 数据源设置获得完整垂直空间。

#### Acceptance Criteria

1. WHEN 日志区域收起, 订单管理页 SHALL 扩展到状态栏上方。
2. WHEN 日志区域展开, 订单管理页 SHALL 在日志区域上方完整显示。
3. WHEN 订单管理页宽度或 DPI 变化, 应用 SHALL 重新计算动态控件尺寸和滚动范围。

### Requirement 5: 状态信息整洁性

**User Story:** AS 操作员, I want 历史记录区域只显示必要信息, so that 状态字符不会干扰表格内容。

#### Acceptance Criteria

1. WHEN 打印页显示历史记录, 历史记录页 SHALL 仅显示历史工具栏和历史表格。
2. WHEN 应用显示连接、今日统计和累计统计, 应用 SHALL 在状态栏和统计页的指定区域显示对应信息。

### Requirement 6: 预览窗口避让

**User Story:** AS 操作员, I want 标签预览与主窗口并排显示, so that 预览期间仍可操作主窗口。

#### Acceptance Criteria

1. WHEN 预览开启且主窗体一侧空间足够, 预览窗 SHALL 停靠在主窗体外侧。
2. WHEN 当前工作区无法容纳主窗体和预览窗原始尺寸, 应用 SHALL 在工作区内平铺主窗体和预览窗。
3. WHEN 主窗体最小化, 应用 SHALL 保留预览停靠边界并暂停位置计算。
4. WHEN DPI、显示器配置、主窗体位置或主窗体尺寸变化, 应用 SHALL 重新计算预览边界。
5. WHEN 预览窗首次显示, 应用 SHALL 在显示前计算初始边界。
