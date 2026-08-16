# 项目文档索引

## 项目概述

BarTenderPrinter 是 Windows 标签打印与订单模板管理工具。主应用负责模板、字段校验、打印提交、历史和补打印；独立预览宿主负责 BarTender SDK 图片导出；SQLite 账本负责幂等、防重和崩溃恢复。

## 文档

- `ARCHITECTURE.md`：桌面应用、预览宿主、打印账本和历史边界。
- `INTERFACES.md`：打印服务、作业协调、账本、历史及补打印契约。
- `DEVELOPER_GUIDE.md`：构建、测试、发布和制品校验流程。
- `USER_OPERATION_GUIDE.md`：打印、订单管理、历史和补打印操作说明。
- `APP_AUDIT_AND_OPTIMIZATION_PLAN.md`：打印应用复核记录。
