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

同步接收的逐条历史继续写入 `PrintRecords`、`FieldValues` 和 `TemplateSnapshots`，以 `RecordId` 幂等合并。远端作业状态写入同一 `print_records.db` 的 `RemotePrintJobEvents` 表，主键为事件身份，并按 `JobId, UpdatedAtUtc` 建索引。

`RemotePrintJobEvents` 是跨设备共享事件表。`print_jobs.db` 是本机作业执行权威，远端事件不参与 `IPrintJobLedger` 的执行状态、幂等、防重、崩溃恢复或补打印裁决。

## 预览宿主

主应用通过 `PreviewHostClient` 启动 `BarTenderPreviewHost.exe`。宿主读取模板和字段快照，调用 BarTender SDK 导出 PNG，并通过标准输出返回结构化结果。超时、进程退出和输出文件校验均由客户端处理。

## 连接与加密

`SyncConnectionProfile` 包含固定坚果云 URL、账号、应用密码、空间 ID、32 字节数据密钥、设备 ID、空间名称、本机直连设置、空间创建者身份、远端基线完成标志和本机捕获启用状态。`SyncWebDavUrlPolicy` 仅允许 `https://dav.jianguoyun.com/dav/` 的默认 443 端口，拒绝用户信息、IP host、其他路径、查询和 fragment。

`SyncConnectionProfileStore.Export()` 和 `Import()` 读写 `.btpsync`。连接文件使用 PBKDF2-HMAC-SHA256 600,000 次派生 256 位密钥，再以 AES-GCM 加密；共享密码不写入文件。`SaveLocal()` 和 `LoadLocal()` 使用 Windows DPAPI CurrentUser 保护 `sync-profile.dat`。

`SyncCrypto.EncryptObject()` 和 `DecryptObject()` 实现 AES-256-GCM v2 对象格式，关联数据绑定空间、类型和对象身份。解密会校验 magic、版本、长度、认证标签和身份；模板及载荷在应用前另行校验 SHA-256。

## 云对象存储

`ICloudObjectStore` 提供 `EnsureCollectionAsync()`、`ListAsync()`、`GetAsync()`、`HeadAsync()` 和 `PutAsync()`。`WebDavObjectStore` 映射到 `MKCOL`、`PROPFIND`、`GET`、`HEAD` 和 `PUT`，支持 `If-None-Match` 创建与 `If-Match` 条件更新，并将认证失败、429、配额和前置条件失败转换为稳定错误码。

对象路径限制在配置的 DAV 根目录内。业务空间使用 `BarTenderPrinterSync/spaces/{space-id}/`，设备记录、事件和模板分别位于 `devices/`、`events/` 和 `templates/`。`snapshots/` 当前仅创建目录。

## 同步存储与协议

`SyncStore` 使用 `sync.db` 持久化：

- `SyncOutbox`：密文事件、状态、重试计数、错误码和下次尝试时间。
- `DeviceCursors` 与 `AppliedEvents`：每台远端设备的连续进度与事件幂等。
- `SyncConflicts` 与 `EntityVersions`：本地/远端版本和处理结果。
- `KnownDevices`：解密后的端点状态和最近结果。
- `SyncUsage` 与 `SyncActivities`：月度计数及最近活动。
- `QuarantinedObjects`：对象路径、安全错误码、首次/最近时间和出现次数，不保存对象内容。
- `SnapshotState`：快照累计事件、密文字节和最近快照基线。

`SyncCoordinator.SynchronizeAsync()` 单实例执行，支持 pull-only 阶段：尝试直连、从 WebDAV 补齐连续事件、逐设备解密并应用、记录冲突，并按流程参数逐项上传 outbox。损坏事件写入 `QuarantinedObjects`，当前设备停在损坏序号，健康设备继续；临时上传错误写入重试时间，认证与同名摘要不一致写入永久阻断状态。`SnapshotManager` 在阈值满足时上传加密快照并条件更新指针。导入连接在验证并保存 profile 后自动执行 pull-only 首次同步，失败时保留配置供重试。

订单和模板设置冲突保存完整远端事件及双方版本。采用远端或保留本地都会生成新的 resolution 事件，其 `BaseVersion` 取双方最高版本，`NewVersion` 再递增；接收设备识别 resolution metadata 后直接应用。打印历史和作业事件维持仅追加约束，逐字段合并继续由差异编辑器提供明确内容。

`SyncDataAdapter.CaptureAsync()` 返回订单、模板设置、逐条打印历史、逐状态作业事件和 SHA-256 内容寻址模板。`FileSnapshotSyncEventApplier` 原子写入订单和设置，逐条合并历史与远端作业事件，并通过 `SharedDataChanged` 通知界面刷新。

## 直连与同步中心

`DeviceEndpointRegistry` 将设备 ID、host、端口、地址优先级、证书指纹、发布时间和 24 小时有效期加密写入 `devices/{device-id}.enc`。`DirectSyncClient` 只使用有效发布端点；`DirectSyncHost` 使用 TLS 1.2/1.3、证书指纹和基于空间密钥的会话认证交换事件清单和密文对象，失败时由协调器回退 WebDAV。

`ISyncPageService` 暴露立即同步、取消、创建空间、导入/导出连接文件、测试 WebDAV、配置/发布/测试直连、解决冲突和导出诊断。`ISyncLifecycleService` 提供启动、共享数据入队、取消等待和退出限时刷新。同步中心显示队列、永久阻断、隔离计数、设备、冲突、用量、活动和当前通道。
