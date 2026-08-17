# 开发者指南

## 环境

- .NET SDK 8。
- Windows 10/11 x64 用于 WinForms 运行与完整测试。
- .NET Framework 4.8 用于 `BarTenderPreviewHost`。
- BarTender 2022 R2 x64 用于真实打印与预览验收。

## 跨平台打印测试

```bash
DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet test BarTenderPrinter.Printing.Tests/BarTenderPrinter.Printing.Tests.csproj -c Release --nologo
```

该项目直接链接打印契约、协调器、SQLite 账本和工作流生产源文件，验证完成结果重放、幂等冲突、并发防重、崩溃恢复和补打印审批规则。

## Linux 交叉构建

```bash
DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet build BarTenderPrinter/BarTenderPrinter.csproj -c Release -p:EnableWindowsTargeting=true --nologo
```

该构建同时编译 `BarTenderPreviewHost`。Linux 环境用于编译验证，Windows runner 执行依赖 Windows Desktop Runtime 的测试。

## Windows 测试

```powershell
dotnet test BarTenderPrinter.Tests/BarTenderPrinter.Tests.csproj -c Release
```

测试覆盖配置模型、订单、数据校验、打印工作流、历史完整性、SQLite 账本、幂等、补打印规则，以及加密对象、`.btpsync`、WebDAV、outbox、cursor、conflict、数据适配、直连和同步中心布局。

同步相关测试位于：

- `SyncCryptoTests.cs`：AES-GCM v2、唯一 nonce、身份认证、连接文件、固定 URL 和 DPAPI。
- `SyncStoreTests.cs`、`SyncStoreWebDavTests.cs`：SQLite 状态、条件请求、安全错误和路径边界。
- `SyncCoordinatorTests.cs`、`SyncApplicationServiceTests.cs`：增量事件、幂等、取消、模板缓存、导入验证和 WebDAV 回退。
- `EndpointSyncDataAdapterTests.cs`：订单、设置、模板、逐条历史和远端作业事件。
- `EndpointCollectorTests.cs`、`DirectSyncTests.cs`：host 过滤、端点优先级、证书、TLS 会话和认证失败。
- `SyncPagePresenterTests.cs`、`SyncLayoutTests.cs`：操作串行化、状态与 DPI 断点。

## 发布

```powershell
dotnet publish BarTenderPrinter/BarTenderPrinter.csproj -c Release -r win-x64 --self-contained true -o publish
./scripts/Test-ReleaseArtifact.ps1 -PublishDirectory publish
```

发布目录必须包含 `BarTenderPrinter.exe` 和 `BarTenderPreviewHost.exe`。校验脚本拒绝证书、私钥、数据库、SQLite、日志、HASP、Sentinel、SafeNet、Hardlock、Dongle 和旧 MobileMes 资产。

Inno Setup 使用 `installer/BarTenderPrinter.iss` 生成当前用户安装包。CI 串行执行跨平台打印测试、Windows 测试、自包含发布、制品扫描和安装包构建。

## 同步开发边界

- 坚果云生产 URL 固定为 `https://dav.jianguoyun.com/dav/`。测试通过内部构造函数和 fake object store 注入隔离端点。
- `print_jobs.db` 是本机执行权威。跨设备作业状态写入 `print_records.db.RemotePrintJobEvents`，只提供共享追溯。
- 订单与全局模板设置采用版本事件；打印历史按 `RecordId`、作业状态按 `JobId + State + UpdatedAtUtc` 逐条同步。
- `.btw` 按明文 SHA-256 去重，订单和设置事件仅传播逻辑模板引用，接收端映射到 `template-cache/{sha256}.btw`。
- 远端对象统一使用 AES-256-GCM v2；连接文件使用 PBKDF2-HMAC-SHA256 600,000 次派生；本机 profile 和直连私钥使用 DPAPI CurrentUser。
- 直连只使用加密设备记录内的 host、端口、有效期和证书指纹，禁止网络扫描；任何端点连接失败后保留 WebDAV 通道。
- `sync.db` 是 outbox、cursor、conflict、隔离对象、快照基线、设备、用量和诊断活动的持久化边界。隔离记录仅允许保存对象路径、稳定安全错误码、首次/最近时间和次数。

## 自动触发与诊断

`MainForm` 在应用启动、网络恢复、共享数据保存后 10 秒防抖以及退出前 5 秒限时执行同步。保存时先调用 `QueueLocalChangesAsync()` 形成持久化 outbox；同步运行由 presenter 和 service 双重串行化，并支持取消。

同步中心的 `导出脱敏诊断` 只输出配置状态、截断后的空间/设备 ID、WebDAV host、队列计数、隔离计数、永久阻断计数、冲突、设备数、月度用量、直连监听状态和模板缓存数量。日志和新增诊断字段必须继续排除账号、应用密码、共享密码、数据密钥、证书私钥、密文和业务字段值。

## 快照与错误恢复

周期快照默认按 500 个累计事件或 20MB 累计密文字节触发，测试可注入较小阈值。快照和 pointer 均使用空间绑定的 AES-GCM 关联数据，pointer 通过 ETag 条件更新；首版保留全部历史事件和快照。outbox 重试优先采用 `Retry-After`，其余临时故障使用最长 30 分钟的指数退避与有界抖动；认证与同名摘要不一致保持永久阻断并显示在同步状态和脱敏诊断中。

## 开发约束

- 外部打印提交使用稳定幂等键和不可变请求快照。
- `Submitting` 状态恢复为 `Uncertain`，由人工核查实际打印结果。
- 补打印保持账户权限、审批、原因、原作业和序号链路。
- 历史格式变更保留旧校验和读取兼容。
- 发布制品保持无数据库、日志和敏感配置。
- 测试、构建和发布串行执行，避免共享输出目录冲突。
- 修改同步协议时同时验证对象身份、连续 cursor、重复上传、冲突和本机打印权威边界。
