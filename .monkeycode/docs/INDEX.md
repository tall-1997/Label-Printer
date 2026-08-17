# 项目文档索引

## 项目概述

BarTenderPrinter 是 Windows 标签打印、订单模板管理和多电脑加密协作工具。主应用负责模板、字段校验、打印提交、历史、补打印和同步中心；独立预览宿主负责 BarTender SDK 图片导出；本机 SQLite 账本负责打印执行可靠性，坚果云 WebDAV 和可选 TLS 专网直连负责交换端到端加密事件与模板对象。

## 文档

- `ARCHITECTURE.md`：桌面应用、打印权威边界、加密同步、WebDAV 与直连架构。
- `INTERFACES.md`：打印、历史、加密对象、连接文件、存储、编排和同步中心契约。
- `DEVELOPER_GUIDE.md`：构建、同步测试、诊断、安全约束、发布和制品校验流程。
- `USER_OPERATION_GUIDE.md`：打印、订单管理、同步中心、连接文件、直连、冲突和恢复操作说明。
- `APP_AUDIT_AND_OPTIMIZATION_PLAN.md`：打印应用复核记录。
