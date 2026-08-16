# BarTender Printer

基于 .NET 8 WinForms 与 Seagull BarTender 接口的 Windows 标签打印工具，提供订单模板管理、字段校验、静默打印、独立预览、历史追溯和受控补打印。

![界面预览](assets/preview.png)

## 最新版本

**v5.7.93** - 纯打印与订单管理精简版

## 核心能力

- 打印页面和订单管理使用统一 MIUIX 风格界面。
- 一个订单可绑定多个 `.btw` 模板，每个模板独立保存数据源、打印机、份数、校验和增降序设置。
- 自动读取模板命名数据源，支持字段排序、锁定、长度校验及 CSV、Excel、TXT 本地数据校验。
- 扫码作业进入本地 FIFO 队列，通过 BarTender COM 接口静默提交。
- 独立 .NET Framework 4.8 x64 预览宿主导出 PNG，隔离 BarTender SDK 生命周期与错误。
- SQLite 打印作业账本在提交前记录不可变请求，提供幂等防重、并发重复提交保护和崩溃恢复。
- 外部提交结果无法确认时记录 `Uncertain`，由操作员核查，避免自动重复打印。
- 历史记录保存 SQLite 主存储、JSONL 兼容副本、CSV 导出和逐条归档副本。
- 补打印保留账户权限、原因、审批 ID、原作业关联、模板版本确认及补打序号。
- `.btw` 文件可通过 Windows 右键菜单直接使用 BarTenderPrinter 打开。

## 技术结构

| 项目 | 说明 |
|---|---|
| `BarTenderPrinter` | .NET 8 WinForms 主应用、BarTender COM 打印、订单、历史和 SQLite 账本 |
| `BarTenderPreviewHost` | .NET Framework 4.8 x64 BarTender SDK 预览宿主 |
| `BarTenderPrinter.Printing.Tests` | 跨平台打印账本、幂等、恢复和补打印规则测试 |
| `BarTenderPrinter.Tests` | Windows 客户端、模型、校验、历史和打印流程测试 |

打印调用链：

```text
订单与字段快照
  -> PrintJobCoordinator
  -> SqlitePrintJobLedger
  -> IBarTenderService.PrintAsync
  -> BarTender COM PrintOut
  -> HistoryManager
```

## 使用流程

1. 选择模板目录和 `.btw` 模板。
2. 在订单管理中添加客户、机型、颜色和订单号，并绑定一个或多个模板。
3. 配置模板数据源、锁定值、增降序、长度、校验文件、打印机和份数。
4. 回到打印页面选择完整订单与模板。
5. 扫码或输入字段，最后一项回车后提交打印。
6. 在历史记录中搜索、导入、导出或执行受控补打印。

## 数据目录

运行数据保存在 `%LOCALAPPDATA%\BarTenderPrinter`：

- `config.ini`：应用配置。
- `orders.json`：订单与模板绑定。
- `print_records.db`：打印历史。
- `print_jobs.db`：打印作业可靠性账本。
- `print_records.jsonl`：历史兼容副本。
- `history-records`：逐条历史归档。
- `previews`：预览缓存。

发布制品排除数据库、日志、证书、私钥、HASP、Sentinel、SafeNet、Hardlock、Dongle 和旧 MobileMes 资产。

## 环境要求

- Windows 10/11 x64。
- BarTender 2022 R2 Automation 或 Enterprise x64。
- 安装包已内置 .NET 8 运行时。

## 构建与测试

Linux 跨平台打印可靠性测试：

```bash
DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet test BarTenderPrinter.Printing.Tests/BarTenderPrinter.Printing.Tests.csproj -c Release
```

Linux 交叉构建 WinForms：

```bash
DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet build BarTenderPrinter/BarTenderPrinter.csproj -c Release -p:EnableWindowsTargeting=true
```

Windows 完整测试与发布：

```powershell
dotnet test BarTenderPrinter.Tests/BarTenderPrinter.Tests.csproj -c Release
dotnet publish BarTenderPrinter/BarTenderPrinter.csproj -c Release -r win-x64 --self-contained true -o publish
./scripts/Test-ReleaseArtifact.ps1 -PublishDirectory publish
```

## 下载

前往 [Releases](https://github.com/tall-1997/Label-Printer/releases) 下载安装包。

## 许可

MIT License
