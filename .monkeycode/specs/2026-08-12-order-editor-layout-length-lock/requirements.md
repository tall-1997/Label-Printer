# Requirements Document

## Introduction

本功能优化订单管理页的数据源长度、锁定和未保存提示交互，降低操作员误保存和误配置风险。

## Glossary

- **全局长度**：订单模板中所有未设置单独长度的数据源使用的长度。
- **单独长度**：数据源明细行中单独设置的长度。
- **锁定图标**：订单管理页数据源明细中用于切换锁定状态的按钮。

## Requirements

### Requirement 1: 长度设置

**User Story:** AS 操作员, I want 全局长度与单独长度有明确覆盖规则, so that 数据源长度设置可预期。

#### Acceptance Criteria

1. WHEN 操作员先设置单独长度再设置全局长度, 系统 SHALL 提示是否覆盖该数据源的单独长度。
2. WHEN 操作员选择覆盖, 系统 SHALL 将该数据源长度更新为全局长度。
3. WHEN 操作员选择跳过, 系统 SHALL 保留该数据源的单独长度。
4. WHEN 操作员先设置全局长度再设置单独长度, 系统 SHALL 对该数据源使用单独长度。

### Requirement 2: 订单管理布局

**User Story:** AS 操作员, I want 订单管理页面聚焦当前订单设置, so that 页面信息减少重复和干扰。

#### Acceptance Criteria

1. WHEN 操作员进入订单管理页面, 系统 SHALL 展示当前打印页订单模板设置。
2. WHEN 操作员点击添加订单, 系统 SHALL 展示添加订单页面并清空非默认项。
3. WHEN 操作员在订单管理页面存在未保存修改并离开, 系统 SHALL 询问是否保存。

### Requirement 3: 锁定交互

**User Story:** AS 操作员, I want 点击图标切换锁定, so that 锁定操作更直观。

#### Acceptance Criteria

1. WHEN 操作员点击锁定图标, 系统 SHALL 切换该数据源锁定状态。
2. WHEN 数据源处于锁定状态, 系统 SHALL 要求保存前填写锁定后输入值。
3. WHEN 数据源处于锁定状态, 系统 SHALL 按输入后锁定保存。
