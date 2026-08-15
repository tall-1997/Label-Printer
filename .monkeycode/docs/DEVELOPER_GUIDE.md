# 开发者指南

## 环境

- .NET SDK 8
- Windows 10/11 x64 用于 WinForms 与 BarTender 集成运行
- .NET Framework 4.8 用于 BarTenderPreviewHost
- PostgreSQL 15 或兼容版本用于中心 MES 持久化

## 跨平台领域测试

```bash
DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet test BarTenderPrinter.Domain.Tests/BarTenderPrinter.Domain.Tests.csproj --nologo
```

当前领域测试项目包含 37 个 `Fact`/`Theory` 测试方法声明，覆盖基础契约、订单、生产单元、号段、路线、包装、质量、返工、出库和归档。

## 模拟设备测试

```bash
DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet test BarTenderPrinter.Devices.Tests/BarTenderPrinter.Devices.Tests.csproj --nologo
```

当前设备测试项目包含 14 个测试方法声明，覆盖电子称稳定读数、超时、格式错误、越界、配置与取消，以及写号成功、失败、回读不一致、未知、配置与取消。

## 中心服务构建

```bash
DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet build BarTenderPrinter.MesApi/BarTenderPrinter.MesApi.csproj --nologo
```

中心服务通过 `ConnectionStrings__MesDatabase` 接收 PostgreSQL 连接字符串。Bearer 工位会话通过 `MesSecurity__Sessions__{index}__Token`、`UserId`、`StationId`、`ShiftId` 和 `Roles__{index}` 注入；令牌由部署环境的受保护配置提供。

## 打印核心测试

```bash
DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet test BarTenderPrinter.Printing.Tests/BarTenderPrinter.Printing.Tests.csproj --nologo
```

该跨平台项目链接打印核心生产源文件，验证 SQLite 幂等账本、崩溃恢复、并发重复提交、四类模板解析和补打审批规则。

## MES 工位客户端测试

```bash
DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet test BarTenderPrinter.MesClient.Tests/BarTenderPrinter.MesClient.Tests.csproj --nologo
```

该跨平台项目链接 MES 客户端生产源文件，包含 6 个测试方法声明，验证瞬态重试保持关联 ID 和幂等键、业务冲突停止重试、日志脱敏、断线意图保留、按原幂等键恢复以及本地/中心冲突进入人工核查。

## PostgreSQL 集成测试

通过项目独立环境变量提供隔离测试数据库连接：

```bash
BARTENDER_TEST_POSTGRES='Host=localhost;Database=bartender_test;Username=project_user;Password=project_password' DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet test BarTenderPrinter.Persistence.Tests/BarTenderPrinter.Persistence.Tests.csproj --nologo
```

测试会运行 v1-v9 版本化迁移，并验证并发号段分配、并发打印领取、包装活动父级约束、过站和打印幂等、订单乐观并发、审计事务回滚、跨订单抽检拒绝、质量冻结恢复、返工上下文、归档不可变性，以及出库确认约束。当前项目包含 18 个 `PostgresFact` 测试方法声明；未设置 `BARTENDER_TEST_POSTGRES` 时，这组真实数据库测试标记为跳过。

## MES API 集成测试

```bash
BARTENDER_TEST_POSTGRES='Host=localhost;Database=bartender_test;Username=project_user;Password=project_password' DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet test BarTenderPrinter.MesApi.Tests/BarTenderPrinter.MesApi.Tests.csproj --nologo
```

测试宿主注入临时工位会话，覆盖 401、403、会话用户/角色/工位/班次完整性、角色策略、审计主体、审计重放去重、IMEI/SN/诊断脱敏、错误响应敏感值隔离、白名单验证、未知枚举、订单和号段查询、并发号码分配、号码幂等重放、认证工位资格、前序工序、组装过站重放、包装绑定冲突、满箱打印意图、打印领取与空队列重放、回执终态、领取工位约束、恢复查询、统一追溯，以及质量处置、出库确认和归档高风险角色隔离。当前项目包含 24 个测试方法声明，其中 23 个为 `PostgresFact`。

## Linux 交叉构建 WinForms

```bash
DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet build BarTenderPrinter/BarTenderPrinter.csproj -p:EnableWindowsTargeting=true --nologo
```

## Windows 客户端测试

```powershell
dotnet test BarTenderPrinter.Tests/BarTenderPrinter.Tests.csproj -c Release
```

该测试项目依赖 Windows Desktop Runtime，覆盖现有 WinForms 客户端、校验、历史、打印协调器和本地账本。

## 构建顺序

共享 Domain 输出会被多个项目引用。当前环境应串行执行领域测试、设备测试、打印核心测试、MES 客户端测试、PostgreSQL 集成测试、MES API 测试、中心服务构建和 WinForms 构建，避免并行进程同时写入共享输出目录。完整 `BarTenderPrinter.Tests` 包含 81 个测试方法声明，仍在 Windows runner 执行。七个测试项目合计 212 个测试方法声明；`Theory` 的数据行会使实际执行用例数高于该声明数。MES API 集成测试禁用测试类并行执行，避免共享 PostgreSQL 队列状态互相干扰。

## CI 分层

`build-csharp.yml` 和 `release-csharp.yml` 先在 `ubuntu-latest` 上启动 PostgreSQL 15，依次执行 Domain、Devices、Printing、MesClient、Persistence 和 MesApi 六个跨平台测试项目并构建中心服务；随后在 `windows-latest` 上执行 `BarTenderPrinter.Tests`，发布 `win-x64` 自包含客户端，校验发布目录并构建 Inno Setup 安装包。Windows 阶段依赖 Linux 阶段成功。

## 启动迁移与配置模板

MES API 在 `app.Run()` 前调用 `PostgresMigrator.MigrateAsync()`，迁移使用事务级 PostgreSQL advisory lock，并按 `schema_migrations` 从 v1 顺序执行到 v9。启动需要 `ConnectionStrings:MesDatabase`。

`BarTenderPrinter.MesApi/appsettings.Template.json` 仅提供 `${PROJECT_POSTGRES_*}`、`${PROJECT_MES_SESSION_TOKEN}`、`${PROJECT_MES_USER_ID}`、`${PROJECT_MES_STATION_ID}`、`${PROJECT_MES_SHIFT_ID}` 和 `${PROJECT_MES_ROLE}` 占位符。部署系统负责注入实际连接与会话值，仓库文档和模板保留占位符。

## 发布制品验证

WinForms 发布目录必须包含 `BarTenderPrinter.exe`、`BarTenderPreviewHost.exe`、`Deployment/workstation-client-contract.json` 和 `Deployment/local-ledger-migrations.sql`。项目文件将两个 Deployment 文件复制到构建和发布输出，Inno Setup 将其安装到 `{app}\Deployment`。

```powershell
./scripts/Test-ReleaseArtifact.ps1 -PublishDirectory publish
```

校验脚本检查必需文件，拒绝包含 `MobileMes` 名称或证书、私钥、数据库、SQLite 和日志扩展名的资产，并扫描配置文件中的非占位敏感值。CI 在生成安装包前执行该脚本。

## 开发约束

- 领域时间使用 UTC。
- 外部重试业务使用稳定幂等键。
- 状态变更携带审计上下文。
- 错误通过稳定代码表达。
- 凭据通过项目独立配置提供，代码和示例文件仅保留占位符。
- PostgreSQL SQL 使用参数化命令，状态更新携带预期版本。
- MobileMes 旧运行时和敏感生产资产保持在隔离分析目录。
