# Requirements Document

## Introduction

本功能统一订单模板的文件引用与数据源设置，修复历史导入后的重复输入、模板字段不一致和打印设置不同步问题，并支持维护订单关联模板。

## Glossary

- **订单模板**：订单号关联的 BarTender `.btw` 文件及独立打印设置。
- **外部绝对路径**：操作员选择的 `.btw` 文件完整路径。
- **空白数据源**：当前打印页面中值为空且需要操作员输入的数据源。

## Requirements

### Requirement 1: 历史数据导入

**User Story:** AS 操作员, I want 打印时只补录历史记录未提供的数据源, so that 已导入字段无需重复输入。

#### Acceptance Criteria

1. WHEN 操作员导入历史字段后点击打印, 系统 SHALL 保留所有非空字段值。
2. WHEN 打印所需数据源仍有空值, 系统 SHALL 仅显示空白且可编辑的数据源。
3. WHEN 所有必填数据源已有值, 系统 SHALL 直接执行校验与打印流程。

### Requirement 2: 模板绝对路径

**User Story:** AS 管理员, I want 订单直接引用外部模板绝对路径, so that 编辑和打印读取同一个模板文件。

#### Acceptance Criteria

1. WHEN 操作员向订单添加模板, 系统 SHALL 保存所选文件的绝对路径。
2. WHEN 系统读取数据源或打印, 系统 SHALL 使用订单模板保存的绝对路径。
3. IF 旧订单仅包含应用目录归档路径, 系统 SHALL 要求操作员重新选择有效外部模板。
4. WHEN 外部模板发生变化, 系统 SHALL 重新读取最新数据源并保留同名字段设置。

### Requirement 3: 订单模板维护

**User Story:** AS 管理员, I want 增删订单号关联的模板, so that 订单配置保持准确。

#### Acceptance Criteria

1. WHEN 操作员添加模板, 系统 SHALL 将模板加入当前订单草稿。
2. WHEN 操作员删除模板, 系统 SHALL 仅删除订单关联关系并保留磁盘文件。
3. IF 删除操作将导致订单没有模板, 系统 SHALL 阻止订单保存并提示至少保留一个模板。

### Requirement 4: 设置同步

**User Story:** AS 操作员, I want 保存的数据源值与锁定状态立即应用到打印页, so that 打印行为与订单编辑设置一致。

#### Acceptance Criteria

1. WHEN 操作员保存订单模板设置, 系统 SHALL 将字段值、锁定状态、锁定方式、增降序和长度设置应用到打印页面。
2. WHEN 操作员重新编辑已更新模板的数据源, 系统 SHALL 展示模板当前数据源集合。
3. WHEN 数据源名称保持一致, 系统 SHALL 保留对应字段的已有设置。
