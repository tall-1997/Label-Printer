# Requirements Document

## Introduction

本功能优化主界面侧边栏交互、校验数据导入流程和打印前校验性能，使操作员在扫码打印时减少弹窗和等待。

## Glossary

- **侧边栏**：用于切换打印页面和订单管理页面的左侧导航区域。
- **本地数据完整匹配**：输入值必须完整存在于导入的本地校验数据集合中。
- **重复校验**：检查本次输入值之间重复，以及当前模板历史记录中已打印的重复值。

## Requirements

### Requirement 1: 侧边栏展开收起

**User Story:** AS 操作员, I want 点击图标展开侧边栏并选择页面后自动收回, so that 页面有更多可用空间。

#### Acceptance Criteria

1. WHEN 操作员点击侧边栏图标, 系统 SHALL 展开侧边栏并显示页面选项。
2. WHEN 操作员选择页面选项, 系统 SHALL 跳转对应页面并收回侧边栏。
3. WHILE 侧边栏展开或收回, 系统 SHALL 保持打印页和订单页控件布局稳定。

### Requirement 2: 本地校验数据导入

**User Story:** AS 操作员, I want 先导入校验数据再启用本地数据完整匹配, so that 校验开关有明确的数据来源。

#### Acceptance Criteria

1. WHILE 本地校验数据为空, 系统 SHALL 禁用本地数据完整匹配开关。
2. WHEN 操作员选择校验数据文件, 系统 SHALL 优先显示 Excel 文件类型。
3. WHEN 操作员选择包含多列的校验数据文件, 系统 SHALL 要求选择用于校验的列。
4. WHEN 操作员启用本地数据完整匹配, 系统 SHALL 对输入值执行完整匹配。

### Requirement 3: 重复校验

**User Story:** AS 操作员, I want 独立启用重复校验, so that 输入重复和历史重复可按需阻止打印。

#### Acceptance Criteria

1. WHEN 重复校验开启, 系统 SHALL 检查本次输入值之间的重复。
2. WHEN 重复校验开启, 系统 SHALL 检查当前模板历史记录中的重复值。
3. WHEN 重复校验关闭, 系统 SHALL 跳过重复校验。

### Requirement 4: 校验性能

**User Story:** AS 操作员, I want 多个校验同时开启时打印前校验快速完成, so that 扫码节奏不受影响。

#### Acceptance Criteria

1. WHEN 导入 CSV 或 TXT 校验数据, 系统 SHALL 使用流式读取构建校验集合。
2. WHEN 执行本地数据完整匹配, 系统 SHALL 使用哈希集合查找。
3. WHEN 执行历史重复校验, 系统 SHALL 使用历史索引查找。
