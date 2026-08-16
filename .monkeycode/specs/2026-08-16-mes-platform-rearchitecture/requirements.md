# MES 平台整体重构需求

## Introduction

本次重构将现有打印工具与 MES 工位集合升级为中心化 MES 平台。平台保留现有 PostgreSQL 一致性、幂等、审计、打印恢复和设备隔离能力，并提供独立的 Web 管理端、任务驱动工位端与 Windows Station Agent 边界。

参考实现：

- `jiujiezongheti/mes-api`：业务域划分、显式权限码、工单快照、库存流水。
- `jiujiezongheti/mes-admin`：职能域导航、搜索偏好、导入导出和可配置报表。
- `Rora-lyt/manufacturing-mes-api-automation`：数据驱动业务场景和跨请求关联测试。

## Glossary

- **中心平台**：ASP.NET Core MES API、PostgreSQL 与 Web 管理端。
- **工位工作台**：面向生产操作员的扫码、过站、包装、称重、写号和打印界面。
- **Station Agent**：运行于 Windows 工位并隔离 BarTender、串口设备、SQLite 恢复和离线同步的本机服务。
- **Capability**：可被后端授权并由前端用于界面呈现的细粒度业务权限。
- **业务上下文**：当前订单、工位、班次、生产单元和操作员组成的工作会话。
- **待中心确认**：离线业务意图已持久化且等待中心权威规则判定的状态。

## Requirements

### Requirement 1：统一平台入口

**User Story:** AS 系统用户, I want 通过浏览器进入统一 MES 平台, so that 我可以在不同终端使用一致的业务能力。

#### Acceptance Criteria

1. WHEN 用户打开中心服务根路径, 中心平台 SHALL 返回可用的 MES Web 应用。
2. WHILE 视口宽度处于 360 至 2560 CSS 像素, MES Web 应用 SHALL 保持主导航、主要操作和业务状态可见。
3. WHEN 用户切换管理模式与工位模式, MES Web 应用 SHALL 保留认证会话和适用的业务上下文。
4. IF Web 静态资源尚未构建, 中心平台 SHALL 保持 API 与健康检查可启动。

### Requirement 2：业务域信息架构

**User Story:** AS 管理人员, I want 按制造业务域访问功能, so that 我可以快速定位计划、生产、质量和仓储任务。

#### Acceptance Criteria

1. MES Web 应用 SHALL 提供概览、订单计划、产品工艺、号码模板、生产执行、质量返工、仓储出库、追溯归档、设备工位和系统权限一级模块。
2. WHEN 用户进入业务模块, MES Web 应用 SHALL 显示模块标题、层级路径、核心指标和首要操作。
3. WHILE 用户缺少模块 Capability, MES Web 应用 SHALL 将模块显示为受限状态并提供权限说明。
4. WHEN 用户返回已访问模块, MES Web 应用 SHALL 恢复筛选、分页和滚动上下文。

### Requirement 3：任务驱动工位

**User Story:** AS 生产操作员, I want 通过扫码和当前任务完成作业, so that 我无需手工录入多个内部 ID。

#### Acceptance Criteria

1. WHEN 工位工作台获得焦点, 工位工作台 SHALL 将扫码输入定位到主扫描通道。
2. WHEN 操作员扫描可识别条码, 工位工作台 SHALL 解析订单、生产单元、包装单元或标识类型并展示下一步允许动作。
3. WHILE 操作处于提交中, 工位工作台 SHALL 禁用重复提交并显示执行进度。
4. WHEN 操作成功, 工位工作台 SHALL 展示结构化结果、关联 ID 和下一步建议。
5. IF 中心规则校验暂时不可用, 工位工作台 SHALL 将允许暂存的业务意图标记为待中心确认。

### Requirement 4：模块化 API

**User Story:** AS 开发人员, I want API 按业务能力组织, so that 单个业务域可以独立演进和测试。

#### Acceptance Criteria

1. 中心平台 SHALL 将订单、号码、生产、包装、打印、质量、返工、仓储、追溯和数据交换端点注册为独立模块。
2. WHEN 客户端调用新版端点, 中心平台 SHALL 使用 `/api/v1` 前缀。
3. WHILE 迁移期有效, 中心平台 SHALL 维持现有 `/api` 契约的业务行为。
4. WHEN 写操作可重试, 中心平台 SHALL 统一校验 `Idempotency-Key` 并计算规范化请求摘要。
5. IF 幂等键与首次请求摘要冲突, 中心平台 SHALL 返回稳定的 `IDEMPOTENCY_CONFLICT` 错误。

### Requirement 5：授权能力与审计

**User Story:** AS 安全管理员, I want 角色、Capability 和资源范围共同控制操作, so that 高风险动作具有最小授权与完整追溯。

#### Acceptance Criteria

1. 中心平台 SHALL 将每个受保护端点关联到明确授权策略。
2. WHEN 客户端请求当前会话, 中心平台 SHALL 返回用户、角色、工位、班次和 Capability 集合。
3. WHEN 状态变更成功, 中心平台 SHALL 在同一事务记录不可变审计事件。
4. WHILE 审计内容包含 IMEI、SN 或设备诊断, 中心平台 SHALL 按现有脱敏规则保存字段。

### Requirement 6：设计系统与无障碍

**User Story:** AS 平台用户, I want 一致且高可读性的界面, so that 长时间生产操作保持清晰和高效。

#### Acceptance Criteria

1. MES Web 应用 SHALL 使用 primitive、semantic 和 component 三层设计令牌。
2. MES Web 应用 SHALL 支持浅色、深色和跟随系统模式。
3. WHEN 用户通过键盘操作, MES Web 应用 SHALL 提供符合视觉顺序的焦点移动与清晰焦点环。
4. WHILE 内容表示成功、警告或失败, MES Web 应用 SHALL 同时使用文本或图标表达状态。
5. MES Web 应用 SHALL 使普通文本与背景达到 WCAG AA 4.5:1 对比度。

### Requirement 7：Station Agent 边界

**User Story:** AS 工位管理员, I want 硬件执行与界面解耦, so that UI 更新不会改变打印和设备可靠性。

#### Acceptance Criteria

1. Station Agent SHALL 作为 BarTender、打印机、电子秤、写号工具和本地 SQLite 的唯一 Web 边界。
2. WHEN 打印请求进入 Station Agent, Station Agent SHALL 在调用 BarTender 前保存不可变打印意图与摘要。
3. IF 打印结果无法确定, Station Agent SHALL 保存 `Uncertain` 状态并要求人工核查。
4. WHEN 中心连接恢复, Station Agent SHALL 使用原幂等键同步待处理业务意图。

### Requirement 8：自动化质量门禁

**User Story:** AS 维护人员, I want 每个重构切片具有自动化证明, so that 新平台可以持续替换旧客户端。

#### Acceptance Criteria

1. WHEN CI 执行, 项目 SHALL 运行领域、设备、持久化、API、Web 单元测试和生产构建。
2. WHEN关键业务场景执行, 测试系统 SHALL 为每个场景创建独立上下文并传播断言失败。
3. WHEN Web E2E 执行, 测试系统 SHALL 覆盖桌面、平板和手机视口的导航与核心工位流程。
4. IF 任一构建、测试或制品门禁失败, CI SHALL 阻止发行任务完成。
