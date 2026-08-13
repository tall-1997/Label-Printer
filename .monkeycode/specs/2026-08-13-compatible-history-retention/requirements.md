# 兼容升级与历史保留需求

## Introduction

本功能修复旧版本升级后的模板数据源交互问题，并将历史删除和清空调整为可审计、非破坏的数据排除操作。系统同时为每条历史记录保存按时间和唯一标识组织的独立副本。

## Glossary

- **活动历史**：参与界面展示、重复校验、统计、预览、导入和补打印调用的历史记录。
- **排除历史**：保留原始数据，同时退出所有业务展示、校验和调用的历史记录。
- **独立历史副本**：应用目录 `history-records` 中按日期和记录 ID 保存的 JSON 文件。
- **旧版数据源配置**：旧 `config.ini` 中的 `DSCount` 和 `DS*` 配置。

## Requirements

### Requirement 1: 静默兼容旧版数据源

**User Story:** AS 升级用户, I want 模板加载时自动兼容旧版数据源, so that 软件启动和切换模板过程保持连续。

#### Acceptance Criteria

1. WHEN 系统加载缺少模板级设置的模板, 系统 SHALL 静默读取模板命名数据源。
2. WHEN 旧版数据源配置存在, 系统 SHALL 按字段名保留同名设置并将结果保存为当前模板设置。
3. WHEN 模板包含新增字段, 系统 SHALL 默认启用新增字段。
4. WHEN 自动数据源读取完成, 系统 SHALL 直接重建输入控件并保持数据源编辑窗口关闭。
5. WHEN 操作员显式点击编辑数据源, 系统 SHALL 显示数据源设置窗口。
6. WHEN 多个异步模板读取请求重叠, 系统 SHALL 只应用最后一次请求结果。

### Requirement 2: 非破坏历史排除

**User Story:** AS 管理员, I want 删除操作保留原始历史, so that 业务校验可重置且审计数据持续存在。

#### Acceptance Criteria

1. WHEN 管理员删除单条活动历史, 系统 SHALL 将该记录标记为排除历史。
2. WHEN 记录成为排除历史, 系统 SHALL 从界面、搜索、统计、重复校验、预览、导入和补打印调用中排除该记录。
3. WHEN 管理员清空当前模板历史, 系统 SHALL 使用同一批次标识排除当前模板全部活动历史。
4. WHEN 排除操作完成, 系统 SHALL 保留 SQLite、JSONL和独立历史副本中的记录内容。
5. WHEN 持久化失败, 系统 SHALL 保持操作前的活动状态并显示错误。

### Requirement 3: 高风险操作提示

**User Story:** AS 管理员, I want 清楚了解清空操作的影响范围, so that 高风险操作具有明确预期。

#### Acceptance Criteria

1. WHEN 管理员请求删除历史, 系统 SHALL 提示记录将退出显示和校验且原始数据继续保留。
2. WHEN 管理员请求清空历史控件, 系统 SHALL 提示当前模板活动记录数量和非破坏语义。
3. WHEN 当前角色缺少历史管理权限, 系统 SHALL 拒绝清空操作并显示权限提示。
4. WHEN 删除或清空成功, 系统 SHALL 记录操作者、UTC 时间、原因和批次标识。

### Requirement 4: 独立历史副本

**User Story:** AS 审计人员, I want 每条历史拥有独立文件副本, so that 可以按时间和唯一标识追溯打印记录。

#### Acceptance Criteria

1. WHEN 系统写入打印历史, 系统 SHALL 在应用本身目录的 `history-records/yyyy/MM/dd` 下保存独立 JSON 副本。
2. WHEN 系统命名独立副本, 系统 SHALL 在文件名中包含打印时间和 `RecordId`。
3. WHEN 系统加载既有历史, 系统 SHALL 为缺少独立副本的记录补建副本。
4. WHEN 同一记录副本已经存在, 系统 SHALL 保留现有副本。
5. IF 独立副本写入失败, 系统 SHALL 记录错误并使本次历史写入失败。
