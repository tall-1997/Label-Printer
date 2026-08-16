# MES 平台整体重构任务清单

## Phase 1：重构基线

- [x] 分析三个参考仓库。
- [x] 审计当前 Domain、Persistence、API、WinForms 和测试边界。
- [x] 确定 React/Vite + ASP.NET Core + Station Agent 目标架构。
- [x] 创建 EARS 需求与技术设计。

## Phase 2：Web 平台纵切面

- [x] 创建 React/TypeScript/Vite 项目。
- [x] 建立 MUI 设计令牌和明暗主题。
- [x] 建立统一 module manifest、响应式 shell 和权限导航。
- [x] 实现运营概览和任务驱动工位。
- [x] 建立类型化 API client、认证会话和 demo fallback。
- [x] 将 Web 构建输出接入 ASP.NET Core 静态托管。

## Phase 3：API 模块化

- [x] 新增 `/api/v1/session` 与 Capability contract。
- [x] 按业务 feature 注册 v1 endpoints。
- [ ] 统一请求摘要和 `Idempotency-Key` 处理。
- [x] 保留旧 `/api` 兼容入口。
- [x] 为 v1 合约增加集成测试。

## Phase 4：应用层与持久化

- [x] 创建 `BarTenderPrinter.Application`。
- [ ] 将订单、号码和打印迁为首批 command/query handler。
- [ ] 抽取统一幂等执行器和审计端口。
- [ ] 收缩 repository 到事务数据访问职责。
- [ ] 消除 Domain 与 SQL 双重业务规则。
- [ ] 将 migration 拆分为独立版本文件。

## Phase 5：Station Agent

- [x] 创建本机 Agent 服务。
- [ ] 提取 Printing 和 MesClient 独立项目。
- [ ] 迁移 BarTender、SQLite 打印账本和待处理操作。
- [ ] 定义 loopback 设备 API。
- [ ] 实现离线 outbox、同步和人工恢复。

## Phase 6：全面迁移

- [ ] 迁移订单、主数据、质量、仓储和追溯管理页面。
- [ ] 迁移过站、包装、称重、写号和打印工位流程。
- [ ] 建立 OpenAPI TypeScript 客户端生成。
- [ ] 建立 Vitest、Testing Library 和 Playwright 门禁。
- [ ] 按工位灰度切换并保留 WinForms 回退版本。
- [ ] 完成真实设备、长时稳定性和现场验收。
