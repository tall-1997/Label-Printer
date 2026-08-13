# Requirements Document

## Introduction

本功能在保持现有打印、订单、历史和日志工作流的前提下，升级桌面应用的视觉层级、品牌识别、控件一致性和高 DPI 表现。

## Glossary

- **应用品牌栏**：主窗口客户区顶部用于展示应用图标、产品名、版本、作者和全局操作的区域。
- **功能图标**：使用统一线宽和视觉尺寸绘制的操作图形。
- **活动页面**：当前显示的打印页面或订单管理页面。

## Requirements

### Requirement 1: 应用品牌栏

**User Story:** AS 操作员, I want 快速识别应用和版本, so that 我能确认当前使用的软件环境。

#### Acceptance Criteria

1. WHEN 主窗口完成初始化, 应用品牌栏 SHALL 显示应用图标、产品名和当前版本。
2. WHEN 应用品牌栏显示, 应用品牌栏 SHALL 显示作者信息 `By---池鱼`。
3. WHEN Windows 显示主窗口标题栏, 主窗口 SHALL 使用应用图标作为窗口图标。
4. WHILE 窗口宽度变化, 应用品牌栏 SHALL 保持品牌信息和右侧操作可见。
5. WHEN 操作员打开关于窗口, 关于窗口 SHALL 显示应用图标、产品名、版本、作者、运行环境、数据目录和项目地址。

### Requirement 2: 现代化视觉体系

**User Story:** AS 操作员, I want 清晰且专业的界面层级, so that 我能快速定位当前任务和关键操作。

#### Acceptance Criteria

1. WHEN 主窗口显示, 应用 SHALL 使用统一的背景、卡片、边框、文字和状态颜色。
2. WHEN 控件承担主要操作, 应用 SHALL 使用高对比品牌色突出控件。
3. WHEN 控件承担次要操作, 应用 SHALL 使用轻量背景和边框表达控件层级。
4. WHEN 表格显示历史记录, 表格 SHALL 使用统一表头、行高、选中状态和网格颜色。
5. WHEN 页面切换, 左侧导航 SHALL 清晰表达活动页面。
6. WHEN 操作员切换日志区域, 应用 SHALL 在日志可见和工作区扩展状态之间重新布局。
7. WHEN 应用显示导航和命令控件, 应用 SHALL 完整显示图标与文字内容。
8. WHEN 左侧导航显示, 左侧导航 SHALL 使用与浅色工作区协调的视觉层级。

### Requirement 3: 图标与操作识别

**User Story:** AS 操作员, I want 通过图标和文字共同识别操作, so that 高频操作更容易扫描。

#### Acceptance Criteria

1. WHEN 全局操作按钮显示, 按钮 SHALL 显示与操作语义一致的线性图标。
2. WHEN 导航按钮显示, 导航按钮 SHALL 显示打印和订单语义图标。
3. WHEN 图标随 DPI 缩放, 图标 SHALL 保持清晰线条和正确比例。
4. IF 图标绘制失败, 按钮 SHALL 保留完整文字标签和可执行行为。

### Requirement 4: 高 DPI 与可访问性

**User Story:** AS 使用高分辨率显示器的操作员, I want 界面保持清晰和完整, so that 我能稳定完成打印任务。

#### Acceptance Criteria

1. WHEN 应用启动, 应用 SHALL 启用 Per-Monitor V2 DPI 模式。
2. WHEN 主窗口 DPI 变化, 动态绘制图标 SHALL 使用当前设备 DPI 重新绘制。
3. WHILE 操作员使用键盘导航, 主要按钮 SHALL 保持可聚焦和可辨识的文字。
4. WHEN 状态变化, 状态栏 SHALL 同时提供颜色指示和文字说明。
5. WHEN 运行时对话框显示, 对话框 SHALL 使用当前显示器 DPI 缩放内容和操作控件。
6. WHEN 预览窗口停靠, 预览窗口 SHALL 使用当前显示器 DPI 计算间距和尺寸范围。

### Requirement 5: 业务兼容

**User Story:** AS 现有用户, I want 视觉升级保持操作流程, so that 我无需重新学习生产操作。

#### Acceptance Criteria

1. WHEN 视觉主题应用, 现有事件处理器 SHALL 保持绑定。
2. WHEN 视觉主题应用, 打印、订单、历史、预览和日志功能 SHALL 保持现有行为。
3. WHEN 主窗口尺寸变化, 打印页面 SHALL 保持现有动态重排能力。
4. IF 外部图标资源不可用, 应用 SHALL 使用程序图标或文字回退。
