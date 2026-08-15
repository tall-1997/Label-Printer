# 接口文档

## 共享基础契约

### EntityId

`BarTenderPrinter.Domain.Common.EntityId` 表示非空、去除首尾空白的实体标识。`EntityId.New()` 创建 32 位 GUID 字符串标识。

### IdempotencyKey

`BarTenderPrinter.Domain.Common.IdempotencyKey` 表示长度 1 至 128 个字符的幂等键。

### OperationResult

`OperationResult<T>` 统一表达成功值或结构化错误。`OperationError` 包含稳定错误码、消息和可重试标记。

当前基础错误码：

- `VALIDATION_FAILED`
- `CONFLICT`
- `NOT_FOUND`
- `UNAUTHORIZED`
- `FORBIDDEN`
- `UNCERTAIN`

### AuditContext

审计上下文包含操作员、工位、班次和关联 ID。`Validate()` 校验操作员与工位。

### IUtcClock

领域服务通过 `IUtcClock.UtcNow` 获取时间。`SystemUtcClock` 返回 UTC 时间。

## 设备边界

`IDeviceAdapter` 提供适配器 ID 和模拟模式标识。

`IScaleAdapter.ReadStableAsync()` 接收串口、波特率、数据切片、单位、稳定读数次数和超时组成的 `ScaleProfile`，返回重量、单位、设备 ID、UTC 采集时间及模拟标识。`SimulatedScaleAdapter` 支持 `StableReading`、`Timeout`、`FormatError` 和 `OutOfRange` 场景；配置、超时和协议错误通过 `DeviceAdapterException` 携带稳定设备错误码。

`IIdentifierWriter.WriteAndVerifyAsync()` 接收任务 ID、标识集合和设备平台快照，返回请求标识、回读标识、工具版本、起止 UTC 时间及 `Succeeded`、`Failed`、`Uncertain` 状态。`SimulatedIdentifierWriter` 支持成功、执行失败、回读不一致和未知结果；成功状态仍会校验完整回读一致性。

## 生产订单

`ProductionOrder` 支持以下状态：

- `Draft`
- `Published`
- `InProduction`
- `Paused`
- `Closed`

订单只在生产中且位于 UTC 有效期内接受过站。

## 号段与分配

`NumberRange` 支持 IMEI、SN、PSN、MSN、卡通箱号和卡板号，编码可组合前缀、日期片段和固定宽度数字。`Allocate()` 在聚合内以锁保护并发分配，并按 `IdempotencyKey` 返回首次结果。

日期格式支持：无日期、YYMM、YYMMDD、YYYYMM、YYYYMMDD、两位年份加年内日、四位年份加年内日和 MMDD。

`NumberAllocation` 生命周期包含已保留、已分配、已释放、已报废和已冻结。

## 生产单元

`ProductionUnit` 管理订单内产品的标识和状态。生产单元支持新建、活动、冻结、报废和完成状态，并保证每种标识类型在单个生产单元内只分配一次。

## 工艺路线与过站

`ManufacturingRoute` 包含有序且唯一的工序集合，支持标准路线和返工路线。`Station` 持有可执行工序资格集合。

`IStationPassService.Pass()` 按以下顺序验证过站：

1. 订单生产状态和有效期。
2. 订单、路线与生产单元关系。
3. 生产单元活动状态。
4. 工序存在性。
5. 工位资格。
6. 返工任务上下文。
7. 工序重复和前序工序。

相同幂等键与相同请求返回首次结果，相同幂等键与不同请求返回 `IDEMPOTENCY_CONFLICT`。前序工序缺失返回 `PREVIOUS_OPERATION_INCOMPLETE` 及缺失工序 ID。

## 返工任务

`ReworkOrder` 支持草稿、已审批、活动、已完成和已取消状态。审批记录审批人和 UTC 时间，完成记录关闭人和 UTC 时间。活动返工路线过站要求返工任务与生产单元和路线一致，并记录返工序次；仓储完成返工前核对返工路线全部工序均已有对应过站记录。

## 质量、出库与归档

`InspectionLot` 保存订单、检验类型、抽样规则和样本生产单元。检验结果只能加入开放抽检单中的样本，同一生产单元和检验项目保持唯一；完成时根据检验结果进入 `Passed` 或 `Failed`。失败结果要求不良代码，失败抽检单可由质量处置审批为 `Release`、`Rework` 或 `Scrap`。

持久化完成失败抽检单时递归冻结关联包装；`Release` 处置将相关 `Frozen` 包装恢复为 `Closed`。`Shipment` 从 `Draft` 经箱号扫描进入 `PendingConfirmation`，仅接收同订单、已关闭且包含机身的卡通箱，并拒绝质量冻结和重复出库。实际数量等于计划数量后才能确认，确认后相关卡通箱更新为 `Shipped`。

`OrderArchiveSnapshot` 仅为已关闭订单创建。归档内容是扩展追溯结果的不可变 JSON，并保存小写 SHA-256 摘要、归档人和 UTC 时间；每个订单最多一个归档快照。

## 包装聚合

`PackagingUnit` 支持机身、彩盒、卡通箱和卡板四种类型。允许的绑定关系为：

- 彩盒包含机身。
- 卡通箱包含彩盒。
- 卡板包含卡通箱。

`PackagingService.Bind()` 验证乐观并发版本、父子类型、订单、型号、颜色、子项关闭状态、父级容量和单活动父级约束。彩盒满容量后自动关闭；卡通箱和卡板满容量后自动关闭并生成 `PackagingPrintIntent`。

打印意图字段快照包含包装码、订单 ID、产品型号、颜色、数量和有序子码集合。字段集合以只读字典暴露。

`PackagingService.Unbind()` 仅处理开放父级，已关闭包装单元进入后续返工流程。

## 持久化边界

`IUnitOfWork.CommitAsync()` 定义中心持久化提交边界。`PostgresOptions` 从项目配置接收连接字符串并创建 `NpgsqlDataSource`，`PostgresMigrator` 在事务级 advisory lock 内按版本执行迁移。

当前 PostgreSQL schema 包含生产订单、号段、号码分配、生产单元、制造路线、工序、工位、工位资格、过站记录、包装单元、包装绑定、包装打印意图、打印作业、打印领取请求、审计事件、抽检单、检验结果、质量处置、返工任务、返工命令、出库单、出库明细、出库命令和订单归档。迁移版本 1 建立核心表和唯一索引，版本 2 增加包装打印意图表，版本 3 增加制造配置表及包装绑定幂等字段和唯一索引，版本 4 增加打印领取与回执字段和索引，版本 5 增加可记录空队列结果的打印领取请求表，版本 6 增加生产单元到机身包装的唯一关联及打印作业追溯外键，版本 7 增加质量、返工、出库和订单归档表、外键、唯一索引及命令幂等记录，版本 8 增加抽检样本订单约束、质量冻结原状态、返工过站上下文约束、抽检完成命令记录及归档更新删除保护，版本 9 安全清理历史跨订单抽检样本并保留修复审计。

仓储职责：

- `ProductionOrderRepository`：创建订单并按版本更新状态。
- `NumberRangeRepository`：创建号段，使用事务和 `SELECT ... FOR UPDATE` 分配号码。
- `ProductionUnitRepository`：保存生产单元和标识快照，并按版本更新状态。
- `ManufacturingConfigurationRepository`：保存制造路线、有序工序、工位和工序资格。
- `StationPassRepository`：在事务锁内验证制造上下文，按幂等键和请求摘要保存过站记录并更新生产单元版本。
- `PackagingRepository`：在事务锁内保存幂等绑定、更新父级状态和版本，并在满箱或满板时保存不可变打印意图。
- `PrintJobRepository`：登记中心打印作业，使用行锁和 `SKIP LOCKED` 并发领取，按领取工位接收幂等回执，并支持作业 ID 与业务幂等键查询。
- `AuditEventRepository`：追加操作主体、关联 ID 和前后 JSON 快照。
- `TraceabilityRepository`：解析订单、IMEI、SN、卡通箱或卡板入口，递归读取包装关系并汇总生产、过站、打印和审计履历。
- `InspectionRepository`：创建抽检单、幂等登记检验结果、乐观并发完成判定、冻结关联包装并执行幂等质量处置。
- `ReworkOrderRepository`：创建返工任务，幂等推进审批、激活与完成状态，并在完成前核对返工路线过站。
- `ShipmentRepository`：校验客户与订单，幂等扫描卡通箱，阻止质量冻结和重复出库，并按计划数量确认出库。
- `ExtendedTraceabilityRepository`：在核心追溯上聚合质量、返工、出库和归档记录。
- `OrderArchiveRepository`：为已关闭订单生成一次性扩展追溯快照和 SHA-256 摘要。

号段值、生产单元 IMEI/SN、包装码、活动父级、打印作业幂等键均由数据库唯一索引约束。版本条件更新冲突抛出 `PersistenceConcurrencyException`；同幂等键异摘要返回 `IDEMPOTENCY_CONFLICT`。

## HTTP 接口

受保护接口使用 `Authorization: Bearer <token>`。项目配置中的工位会话必须同时包含用户、至少一个角色、工位和班次。`StationSessionFilter` 在业务端点执行前校验并缓存会话上下文，写操作只从该认证上下文读取操作员、工位和班次。策略包含 `Planner`、`ProcessEngineer`、`NumberAllocator`、`StationOperator`、`ReprintApprover`、`ReworkOperator`、`ReworkApprover`、`QualityOperator`、`DispositionApprover`、`ArchiveOperator`、`ShipmentConfirmer` 和 `WarehouseOperator`。质量处置要求 `QualityManager`，返工审批与完成要求 `ProductionSupervisor`，归档要求 `ArchiveAdministrator`，出库确认要求 `WarehouseSupervisor`。

`AuditSnapshot` 为状态变更生成统一审计事件。审计事件包含认证用户、工位、班次、关联 ID、动作、关联实体及前后 JSON 快照；IMEI、SN 和设备诊断字段在入库前递归替换为 `***`。幂等重放沿用首次业务结果，不重复追加审计事件。

统一错误响应包含稳定 `code`、操作员可读 `message`、`correlationId`、`retryable` 和可选 `details`。响应头同时返回 `X-Correlation-ID`。认证、权限、参数绑定、业务冲突和服务异常使用同一错误结构。

### GET /health

返回中心服务健康状态：

```json
{
  "status": "healthy"
}
```

### POST /api/orders

创建生产订单，需要 `Planner` 角色。保存订单编号、客户、产品型号、颜色、计划数量和 UTC 有效期，并追加创建审计事件。

### GET /api/orders/{id}

查询订单基础字段、状态、版本、计划数量、完成数量和异常数量。

### POST /api/number-ranges

创建号段，需要 `ProcessEngineer` 角色。支持号码类型、固定前缀、日期片段、范围、步长、数字宽度和校验正则。

### GET /api/number-ranges/{id}

查询号段配置、下一个数值、耗尽状态和版本。

### POST /api/number-ranges/{id}/allocations

事务申请下一个号码，需要 `ProcessEngineer` 或 `ProductionOperator` 角色。请求包含 `idempotencyKey`；服务端根据号段、幂等键、操作员和工位计算摘要。相同请求返回首次号码并设置 `isReplay=true`，异请求复用键返回 `IDEMPOTENCY_CONFLICT`。

### POST /api/station-passes

执行组装过站，需要 `ProductionOperator` 或 `PackagingOperator` 角色。请求包含生产单元、订单、路线、工序、幂等键和可选返工上下文；操作员和工位取自认证会话。服务端按制造规则验证并更新生产单元版本。相同请求重放返回首次记录并设置 `isReplay=true`，缺少前序工序时返回 `PREVIOUS_OPERATION_INCOMPLETE` 和 `missingOperationId`。

### POST /api/packaging-bindings

将子级包装单元绑定到父级，需要 `ProductionOperator` 或 `PackagingOperator` 角色。请求包含父级、子级、预期父级版本和幂等键。服务端验证活动父级、包装层级、产品属性、子级关闭状态、容量及版本；父级达到容量时自动关闭，卡通箱或卡板响应包含不可变 `printIntent`。相同请求重放设置 `isReplay=true`，同键异请求返回 `IDEMPOTENCY_CONFLICT`。

### POST /api/print-jobs/claims

领取最早的 `Received` 打印作业，需要工位操作角色。服务端从认证会话写入领取工位与操作员，并将作业转换为 `Submitting`。请求携带幂等键；相同领取请求返回首次作业并设置 `isReplay=true`。空队列返回 `204 No Content`，该空结果也按领取幂等键持久化。

### POST /api/print-jobs/{jobId}/receipts

提交打印结果，需要工位操作角色。回执只接受 `Submitted`、`Failed` 或 `Uncertain`，`result` 必须为不超过 65536 字节的 JSON 对象。回执工位必须与领取工位一致；相同回执重放返回首次结果，同键异请求或其他终态返回冲突。

### GET /api/print-jobs/{jobId}

按中心打印作业 ID 查询完整同步状态，用于本地恢复和人工核查。

### GET /api/print-jobs/by-idempotency-key/{key}

按打印业务幂等键查询中心结果，用于工位客户端断线恢复后同步本地账本。

### GET /api/traceability

统一生产追溯查询。查询参数 `type` 只接受 `Order`、`Imei`、`SerialNumber`、`Carton` 或 `Pallet`，`value` 为对应业务标识。订单查询支持订单 ID 或订单号；其他类型按精确业务值查询。响应包含订单、关联生产单元及标识快照、过站记录、完整包装链、包装打印意图、中心打印作业、审计事件、检验批、检验结果、处置、返工、出库及归档记录。未知查询类型返回 `VALIDATION_FAILED`，未命中记录返回 `NOT_FOUND`。

### 质量接口

- `POST /api/inspection-lots`：创建抽检单，要求 `QualityOperator`。
- `POST /api/inspection-lots/{lotId}/results`：幂等登记样本检验结果，要求 `QualityOperator`。
- `POST /api/inspection-lots/{lotId}/complete`：按预期版本完成抽检判定，要求 `QualityOperator`。
- `POST /api/inspection-lots/{lotId}/disposition`：审批失败抽检单处置，要求 `DispositionApprover`。

### 返工接口

- `POST /api/rework-orders`：创建返工任务，要求 `ReworkOperator`。
- `POST /api/rework-orders/{id}/approve`：幂等审批返工任务，要求 `ReworkApprover`。
- `POST /api/rework-orders/{id}/activate`：幂等激活返工任务，要求 `ReworkOperator`。
- `POST /api/rework-orders/{id}/complete`：核对路线并幂等完成返工，要求 `ReworkApprover`。

### 出库与归档接口

- `POST /api/shipments`：创建出库单，要求 `WarehouseOperator`。
- `POST /api/shipments/{id}/cartons`：幂等扫描卡通箱，要求 `WarehouseOperator`。
- `POST /api/shipments/{id}/confirm`：幂等确认计划数量，要求 `ShipmentConfirmer`。
- `POST /api/orders/{orderId}/archive`：为已关闭订单创建归档，要求 `ArchiveOperator`。
- `GET /api/orders/{orderId}/archive`：读取订单归档，要求有效工位会话。

## WinForms MES 客户端契约

`MesConnectionOptions` 配置绝对 HTTP/HTTPS 地址、1 至 120 秒超时和 0 至 3 次重试。`MesApiClient` 为每次逻辑请求生成 `X-Correlation-ID`，发送 Bearer 会话和可选 `Idempotency-Key`；GET 与带幂等键的请求可在超时、429 和 5xx 时重试。日志只记录隐藏查询值后的路径。

`MesWorkstationService` 提供健康检查、订单查询、组装过站、包装绑定、打印领取与回执、打印恢复和追溯查询。连接配置保存到 `mes-connection.json`，业务意图和打印恢复快照保存到 `mes-pending-operations.json`。过站与包装保持在线校验，连接失败时保留 `Pending` 意图并返回 `ONLINE_VALIDATION_REQUIRED`。打印恢复按原始业务幂等键查询中心；本地与中心状态一致时转为 `Synced`，冲突时保留双方快照并转为 `ReviewRequired`。

## 现有打印边界

`IBarTenderService` 负责模板字段、预览、打印和打印机枚举。`IHistoryRepository` 负责本地打印历史。`PrintJobCoordinator` 统一打印请求快照、BarTender 提交和历史写入。

### 打印业务契约

`PrintJobRequest` 和 `PrintHistoryEntry` 包含 `JobId`、`IdempotencyKey`、`BatchId`、`BatchItemId`、`LabelType`、`OriginalJobId`、`ApprovalId` 和 `ReprintSequence`。`LabelType` 支持机身标、彩盒标、卡通箱标和卡板标。

补打请求必须包含原作业 ID、审批 ID、原因和大于零的补打序号。缺失字段返回 `REPRINT_APPROVAL_REQUIRED`，且不会调用 BarTender。

### IPrintJobLedger

`IPrintJobLedger` 提供以下操作：

- `Register()`：以幂等键和 SHA-256 请求摘要保存 `Received` 快照。
- `TryMarkSubmitting()`：原子领取待提交作业。
- `Complete()`：保存 `Submitted`、`Failed` 或 `Uncertain` 完成结果。
- `Get()`：按幂等键查询本地状态。

`SqlitePrintJobLedger` 使用独立的 `print_jobs.db`。同键同摘要返回首次结果，同键异摘要返回 `IDEMPOTENCY_CONFLICT`。进程启动时，遗留的 `Submitting` 状态转换为 `Uncertain`；遗留的 `Received` 状态可以继续领取。

### LabelTemplateRegistry

`LabelTemplateRegistry.Resolve()` 按客户、产品型号、标签类型和 UTC 生效时间选择最新生效版本。注册项要求模板 ID、路径、版本和有效时间完整。
