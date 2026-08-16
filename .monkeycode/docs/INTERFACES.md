# 接口文档

## 打印服务

`IBarTenderService` 提供连接、模板数据源读取、打印机枚举、同步与异步打印、预览导出和诊断接口。主应用通过该接口调用 `BarTenderService`，测试使用替身验证工作流。

`PrintAsync(templatePath, fieldValues, printer, copies)` 返回 `PrintResult`。状态包含：

- `Submitted`：外部提交已确认。
- `Failed`：提交失败且可安全重试。
- `Uncertain`：提交可能已到达外部系统，需要人工核查。

## 打印作业

`PrintJobRequest` 包含作业 ID、幂等键、普通打印或补打印类型、模板快照、字段快照、打印机、份数、操作员、订单和模板字段。

补打印还包含：

- `OriginalJobId`：原打印作业。
- `ApprovalId`：本次补打印审批标识。
- `ReprintReason`：补打印原因。
- `ReprintSequence`：原记录后的递增补打序号。

`PrintJobCoordinator.ExecuteAsync()` 按顺序登记账本、提交 BarTender、保存历史并完成账本。返回 `PrintJobCompletion`，包含打印结果、历史保存状态、作业标识、重放标记和账本状态。

## 幂等账本

`IPrintJobLedger` 提供：

- `Register()`：登记不可变请求及 SHA-256 摘要。
- `TryMarkSubmitting()`：原子进入外部提交阶段。
- `Complete()`：保存最终结果。
- `Get()`：读取已登记作业。

`SqlitePrintJobLedger` 使用幂等键作为主键，并对作业 ID 建立唯一索引。相同键与不同摘要返回冲突。

## 打印历史

`IHistoryRepository` 提供加载、新增、搜索、统计、重复值检测、排除和导出能力。`HistoryManager` 使用 SQLite 作为主存储，并维护 JSONL 与逐条归档副本。

`PrintHistoryEntry` 保存作业 ID、幂等键、补打印审批链、模板、字段、结果、打印机、操作员、订单和诊断信息。历史 schema v5 保留 v4 读取与校验和兼容。

## 预览宿主

主应用通过 `PreviewHostClient` 启动 `BarTenderPreviewHost.exe`。宿主读取模板和字段快照，调用 BarTender SDK 导出 PNG，并通过标准输出返回结构化结果。超时、进程退出和输出文件校验均由客户端处理。
