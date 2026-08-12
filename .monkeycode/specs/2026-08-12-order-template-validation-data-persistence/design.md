# 订单模板校验数据持久化

Feature Name: order-template-validation-data-persistence
Updated: 2026-08-12

## Description

本地校验数据从模板设置内联集合迁移为应用数据目录下的快照文件。模板设置保存导入源路径、快照文件路径和列名。快照文件命名基于订单号范围、模板 ID 和模板路径哈希，确保同一订单不同模板独立生效。

## Architecture

```mermaid
flowchart LR
    A["导入校验数据"] --> B["HashSet 去重"]
    B --> C["保存到 validation-data"]
    C --> D["TemplateSettings.LocalDataStoragePath"]
    D --> E["切换模板恢复"]
    E --> F["本地完整匹配"]
```

## Components and Interfaces

- `AppPaths.ValidationDataDirectory`：保存校验数据快照。
- `TemplateSettings.LocalDataStoragePath`：记录快照文件路径。
- `TemplateSettings.LocalDataColumnName`：记录导入列名。
- `MainForm.GetTemplateLocalData`：从快照文件恢复校验集合，并兼容旧版内联数据。

## Data Models

- 快照文件：UTF-8 文本，每行一个用于完整匹配的值。
- 快照键：`订单范围 | 模板 ID | 模板路径` 的 SHA-256 前缀。

## Correctness Properties

- 取消本地完整匹配不会删除快照文件。
- 重新导入会更新当前模板设置中的快照路径。
- 同一订单号下不同模板恢复不同校验集合。
- 模板下拉显示文件名和父目录，减少路径相近模板误选。

## Error Handling

- 快照文件缺失时回退到旧版内联 `LocalData`。
- 应用目录不存在时初始化创建。

## Test Strategy

- 对同一订单两个模板分别导入不同校验数据并切换验证恢复。
- 取消本地完整匹配后重新勾选验证无需重新导入。
- 重新导入后验证新数据生效。
- 离线网络环境启动应用验证侧边栏图标可用。
