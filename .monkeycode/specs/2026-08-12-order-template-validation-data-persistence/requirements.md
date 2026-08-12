# Requirements Document

## Introduction

本功能优化本地校验数据的保存、生效范围和模板切换恢复能力。校验数据按订单号与模板组合生效，导入后保存到应用数据目录，取消勾选仅关闭本地完整匹配功能并保留数据。

## Glossary

- **本地校验数据**：操作员导入的 Excel、CSV 或文本数据集合。
- **生效范围**：订单号与模板共同确定的数据适用范围。
- **模板设置**：订单模板对应的数据源、锁定、校验和打印配置。

## Requirements

### Requirement 1: 校验数据持久化

**User Story:** AS 操作员, I want 导入的校验数据保存到应用目录, so that 切换模板或重启后无需重复导入。

#### Acceptance Criteria

1. WHEN 操作员导入校验数据, 系统 SHALL 将校验数据保存到应用数据目录。
2. WHEN 操作员切换回同一订单号和模板, 系统 SHALL 恢复已导入校验数据。
3. WHEN 操作员重新导入校验数据, 系统 SHALL 使用新数据替换当前订单模板的数据引用。

### Requirement 2: 生效范围

**User Story:** AS 操作员, I want 校验数据只作用于当前订单号和模板, so that 多模板订单之间互不影响。

#### Acceptance Criteria

1. WHEN 当前订单号下存在多个模板, 系统 SHALL 为每个模板保存独立校验数据引用。
2. WHEN 操作员切换模板, 系统 SHALL 即时加载目标模板的校验数据和生效状态。
3. WHEN 操作员取消本地完整匹配, 系统 SHALL 保留导入数据并仅关闭校验生效状态。

### Requirement 3: 多模板识别

**User Story:** AS 操作员, I want 在打印页清楚区分同一订单号下的不同模板, so that 能选择正确模板打印。

#### Acceptance Criteria

1. WHEN 打印页列出订单模板, 系统 SHALL 显示模板文件名和父目录名称。
2. WHEN 操作员选择模板, 系统 SHALL 加载对应路径、校验数据和模板设置。

### Requirement 4: 内置图标

**User Story:** AS 操作员, I want 侧边栏图标随应用可用, so that 网络不可用时也能展开侧边栏。

#### Acceptance Criteria

1. WHEN 应用启动, 系统 SHALL 使用内置轻量图标绘制侧边栏入口。
2. IF 网络不可用, 系统 SHALL 保持侧边栏入口可点击。
