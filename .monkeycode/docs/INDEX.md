# 项目文档索引

## 项目概述

BarTenderPrinter 是面向包装 MES 场景的 Windows 标签打印客户端。当前代码包含 WinForms MES 工位页面与断线恢复、模拟电子称和写号设备、PostgreSQL v9 中心持久化、带工位会话与角色授权的 MES API，以及订单、号段、过站、包装、打印、质量、返工、出库、归档和扩展追溯闭环。CI 将 Linux 中心服务与跨平台测试、Windows WinForms/BarTender 测试和安装包构建分层执行。

## 文档

- `ARCHITECTURE.md`：系统结构、项目职责、MES 客户端恢复边界与扩展领域状态。
- `INTERFACES.md`：领域和设备契约、PostgreSQL v1-v9、HTTP 端点及 WinForms MES 客户端契约。
- `DEVELOPER_GUIDE.md`：七个测试项目、CI 分层、启动迁移、配置模板和发布制品验证。
- `USER_OPERATION_GUIDE.md`：WinForms 打印、订单管理和 MES 工位操作说明。
- `APP_AUDIT_AND_OPTIMIZATION_PLAN.md`：现有打印应用复核记录。

## 功能规格

MES 核心集成规格位于 `.monkeycode/specs/2026-08-15-mes-core-integration/`：

- `requirements.md`
- `design.md`
- `tasklist.md`
