# 开发者指南

## 环境

- .NET SDK 8
- Windows 10/11 x64 用于 WinForms 与 BarTender 集成运行
- .NET Framework 4.8 用于 BarTenderPreviewHost
- PostgreSQL 16 用于中心 MES 持久化和集成测试

## 跨平台领域测试

```bash
DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet test BarTenderPrinter.Domain.Tests/BarTenderPrinter.Domain.Tests.csproj --nologo
```

当前领域测试项目覆盖基础契约、订单状态、生产单元、号段与号码生命周期、路线、包装、称重规则、质量、返工、出库和归档。仓库中未保留本轮 Domain 测试执行日志，因此文档不固化执行用例总数。

## 模拟设备测试

```bash
DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet test BarTenderPrinter.Devices.Tests/BarTenderPrinter.Devices.Tests.csproj --nologo
```

本轮跨平台测试结果为 36 个用例通过，覆盖电子称稳定读数、超时、格式错误、越界、配置与取消，以及写号成功、失败、回读不一致、未知、配置与取消。测试对象为模拟适配器和真实适配接口契约。

## 中心服务构建

```bash
DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet build BarTenderPrinter.MesApi/BarTenderPrinter.MesApi.csproj --nologo
```

中心服务通过 `ConnectionStrings__MesDatabase` 接收 PostgreSQL 连接字符串。Bearer 工位会话通过 `MesSecurity__Sessions__{index}__Token`、`UserId`、`StationId`、`ShiftId` 和 `Roles__{index}` 注入；令牌由部署环境的受保护配置提供。

## 打印核心测试

```bash
DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet test BarTenderPrinter.Printing.Tests/BarTenderPrinter.Printing.Tests.csproj --nologo
```

本轮跨平台测试结果为 7 个用例通过，验证 SQLite 幂等账本、崩溃恢复、并发重复提交、四类模板解析和补打审批规则。

## MES 工位客户端测试

```bash
DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet test BarTenderPrinter.MesClient.Tests/BarTenderPrinter.MesClient.Tests.csproj --nologo
```

本轮跨平台测试结果为 32 个用例通过，验证瞬态重试、关联 ID 与幂等键稳定、日志脱敏、订单/主数据/号码/称重/写号/质量/返工/出库/归档/CSV 请求、断线意图保留、重新提交、人工处理和打印恢复。

## PostgreSQL 集成测试

通过项目独立环境变量提供隔离测试数据库连接：

```bash
BARTENDER_TEST_POSTGRES='Host=localhost;Database=bartender_test;Username=project_user;Password=project_password' DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet test BarTenderPrinter.Persistence.Tests/BarTenderPrinter.Persistence.Tests.csproj --nologo
```

测试面向 PostgreSQL 16 运行 v1-v17 版本化迁移，并验证并发号段分配、自动四类标签作业、号码状态、称重、写号工位隔离、质量处置任务、返工、出库、归档修复、CSV 交换、审计与幂等。本轮结果为 26 个用例通过；未设置 `BARTENDER_TEST_POSTGRES` 时，真实数据库测试标记为跳过。

## MES API 集成测试

```bash
BARTENDER_TEST_POSTGRES='Host=localhost;Database=bartender_test;Username=project_user;Password=project_password' DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet test BarTenderPrinter.MesApi.Tests/BarTenderPrinter.MesApi.Tests.csproj --nologo
```

测试宿主注入临时工位会话，覆盖 401、403、完整会话、角色策略、审计与脱敏、订单状态、主数据、号码处置、称重与写号、四类自动标签作业、过站包装、质量处置、返工、出库、归档修复、CSV、打印恢复和统一追溯。本轮结果为 29 个用例通过。

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

共享 Domain 输出会被多个项目引用，验证时应串行执行各测试项目以避免并行进程写入共享输出目录。当前已确认的跨平台执行结果为 Domain 43、Devices 36、Printing 7、MesClient 32、Persistence 26、MesApi 34，共 178 项。Windows `BarTenderPrinter.Tests` 继续由 Windows runner 执行。MES API 集成测试禁用测试类并行执行，避免共享 PostgreSQL 队列状态互相干扰。

## CI 分层

CI 的目标数据库基线为 PostgreSQL 16。流水线依次执行 Domain、Devices、Printing、MesClient、Persistence 和 MesApi 六个跨平台测试项目；Windows 阶段执行 `BarTenderPrinter.Tests`、发布 `win-x64` 客户端并验证制品。工作流配置升级 PostgreSQL 镜像时应与本指南保持一致。

## 启动迁移与配置模板

MES API 在 `app.Run()` 前调用 `PostgresMigrator.MigrateAsync()`，迁移使用事务级 PostgreSQL advisory lock，并按 `schema_migrations` 从 v1 顺序执行到 v17。启动需要 `ConnectionStrings:MesDatabase`。

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
- 设备首期只交付模拟适配器与真实适配接口，开发文档和发布说明不得宣称真实硬件适配已完成。
- 项目范围排除 HASP/Sentinel 商业授权、加密狗驱动和许可证服务器集成。
- 客户端账户密码继续使用 PBKDF2-SHA256 加盐派生，MES API 继续使用 Bearer 工位会话、角色策略、审计快照和敏感字段脱敏。
