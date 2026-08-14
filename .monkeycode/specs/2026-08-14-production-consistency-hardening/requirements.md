# 生产一致性与运行安全修复需求

## Introduction

本功能修复打印状态提交、订单实体标识、账户恢复、资源等待、窗口关闭和多 DPI 布局中的生产一致性问题。

## Glossary

- **打印快照**：打印作业入队时保存的模板、字段值、打印机、订单和操作员信息。
- **成功状态提交**：打印服务返回成功后执行字段锁定、自动增序、输入清理和配置保存。
- **稳定订单标识**：订单创建后跨业务字段编辑保持不变的 `OrderId`。
- **账户恢复错误**：账户文件存在但无法解析或缺少有效账户的状态。

## Requirements

### Requirement 1: 打印结果与输入状态一致

**User Story:** AS 操作员, I want 输入状态仅随成功打印推进, so that 失败作业保留可重试数据。

#### Acceptance Criteria

1. WHEN 普通打印作业入队, 主应用 SHALL 保留打印快照并暂停新的普通打印提交。
2. WHEN 打印服务返回成功, 主应用 SHALL 使用打印快照提交锁定值、自动增序和输入清理。
3. WHEN 打印服务返回失败, 主应用 SHALL 保留当前输入状态并记录失败历史。
4. WHEN 打印服务返回结果, 主应用 SHALL 在刷新界面前持久化 PASS 或 FAIL 历史。
5. WHILE 普通打印作业未完成, 主应用 SHALL 保持主窗口打开并提示等待作业结束。

### Requirement 2: 订单标识稳定

**User Story:** AS 订单管理员, I want 编辑订单后保留原订单标识, so that 历史和模板设置持续关联同一订单。

#### Acceptance Criteria

1. WHEN 创建订单, 主应用 SHALL 生成非空订单标识。
2. WHEN 编辑已有订单, 主应用 SHALL 将原订单标识写入保存后的订单。
3. WHEN订单业务字段变化, 主应用 SHALL 将模板设置和校验数据快照关联到原订单标识。

### Requirement 3: 显式账户认证与恢复

**User Story:** AS 管理员, I want 受保护操作基于显式登录, so that 启动应用不会自动获得管理权限。

#### Acceptance Criteria

1. WHEN 主应用启动, 主应用 SHALL 使用 Operator 会话并等待显式账户登录。
2. WHEN 登录窗口显示, 主应用 SHALL 显示空密码输入框。
3. IF 账户文件解析失败, 账户管理器 SHALL 保留原文件内容并报告账户恢复错误。
4. WHEN 账户文件首次创建, 账户管理器 SHALL 创建兼容账户文件。

### Requirement 4: 多 DPI 可操作布局

**User Story:** AS 操作员, I want 配置对话框和预览窗口保持在工作区内, so that 高 DPI 环境中的全部控件可访问。

#### Acceptance Criteria

1. WHEN 数据源对话框显示, 主应用 SHALL 将窗口边界限制在当前显示器工作区。
2. WHEN 对话框内容宽于可用区域, 主应用 SHALL 提供横向和纵向滚动访问。
3. WHEN 主窗口与预览平铺, 主应用 SHALL 优先满足主窗口的 DPI 感知最小宽度。
4. IF 水平工作区无法容纳两个窗口, 主应用 SHALL 保持主窗口完整并将预览覆盖停靠在工作区右侧。
