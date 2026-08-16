# 项目文档索引

## 项目概述

BarTenderPrinter 是面向包装 MES 场景的 Windows 标签打印客户端与中心服务。当前实现包含完整 MIUIX WinForms MES 页面、PostgreSQL 16 中心持久化及 v1-v17 迁移、工位认证和角色授权、订单状态与主数据、号码生命周期、称重与写号任务、四类标签自动作业、质量处置、返工、出库、归档修复、CSV 导入导出、追溯及操作恢复。首期设备层提供模拟电子称和模拟写号适配器，同时保留真实设备适配接口。

项目明确排除 HASP/Sentinel 等商业授权、加密狗许可和许可证服务器模块；现有登录认证、PBKDF2-SHA256 密码存储、工位 Bearer 会话、角色授权、审计记录与敏感字段脱敏继续保留。

## 文档

- `ARCHITECTURE.md`：系统结构、项目职责、自动作业、设备边界、安全边界与恢复设计。
- `INTERFACES.md`：领域和设备契约、PostgreSQL v1-v17、HTTP 端点及 WinForms MES 客户端契约。
- `DEVELOPER_GUIDE.md`：测试项目、PostgreSQL 16、启动迁移、配置模板和开发边界。
- `USER_OPERATION_GUIDE.md`：完整 MIUIX WinForms 打印、MES 工位、导入导出与恢复操作说明。
- `APP_AUDIT_AND_OPTIMIZATION_PLAN.md`：现有打印应用复核记录。

## 功能规格

MES 核心集成规格位于 `.monkeycode/specs/2026-08-15-mes-core-integration/`：

- `requirements.md`
- `design.md`
- `tasklist.md`
