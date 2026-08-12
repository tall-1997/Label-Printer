# Requirements Document

## Introduction

本功能优化打印页面和订单管理页面布局，保留打印机与份数编辑能力，隐藏打印页中的配置管理入口，并在退出软件时增加确认提示。

## Glossary

- **打印页**：操作员执行扫码输入、模板选择和打印操作的页面。
- **订单管理页**：操作员新增订单和编辑订单模板设置的页面。
- **配置入口**：保存配置、加载配置、校验数据导入、诊断、校验开关、全局长度设置等管理控件。

## Requirements

### Requirement 1: 打印页布局

**User Story:** AS 操作员, I want 打印页只保留打印必要控件, so that 操作路径清晰。

#### Acceptance Criteria

1. WHEN 操作员进入打印页, 系统 SHALL 显示订单选择、模板选择、打印机、份数、输入区、打印按钮和历史统计区域。
2. WHEN 操作员进入打印页, 系统 SHALL 隐藏配置入口。
3. WHEN 操作员选择订单模板, 系统 SHALL 只加载订单模板设置并保持锁定状态只读。

### Requirement 2: 订单管理布局

**User Story:** AS 操作员, I want 订单管理页用于设置维护, so that 打印页和设置页职责清晰。

#### Acceptance Criteria

1. WHEN 操作员进入订单管理页, 系统 SHALL 展示当前打印订单的模板设置。
2. WHEN 操作员选择已有订单, 系统 SHALL 加载该订单的模板设置。
3. WHEN 订单切换失败, 系统 SHALL 回滚选择状态。

### Requirement 3: 退出确认

**User Story:** AS 操作员, I want 退出软件前确认, so that 避免误关闭正在使用的打印工具。

#### Acceptance Criteria

1. WHEN 操作员关闭主窗口, 系统 SHALL 弹窗确认是否退出软件。
2. WHEN 操作员取消退出, 系统 SHALL 保持软件运行。
3. WHEN 订单管理页存在未保存修改, 系统 SHALL 在退出前提示是否保存设置。
