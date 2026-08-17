# BarTender Printer

基于 .NET 8 WinForms 与 Seagull BarTender 接口的 Windows 标签打印工具，提供订单模板管理、字段校验、静默打印、独立预览、历史追溯、受控补打印和端到端加密的多电脑同步。

![界面预览](assets/preview.png)

## 最新版本

**v5.7.94** - 加密 WebDAV 多设备同步版

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
- 同步中心通过坚果云固定地址 `https://dav.jianguoyun.com/dav/` 交换 AES-256-GCM v2 密文，支持创建空间以及导入、导出 `.btpsync` 连接文件。
- 订单、全局模板设置、按内容寻址的 `.btw` 模板、逐条打印历史和远端作业状态可增量同步；可选 TLS 专网直连会校验证书指纹和空间身份，端点不可达时继续使用 WebDAV。

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

同步调用链：

```text
orders.json / template_settings.json / print_records.db / print_jobs.db
  -> SyncDataAdapter
  -> AES-GCM v2 事件与模板对象
  -> sync.db outbox / cursor / conflict
  -> 可选 TLS 专网直连
  -> 坚果云 WebDAV 密文目录
```

`print_jobs.db` 始终是当前电脑打印执行、幂等、防重、`Uncertain` 和崩溃恢复的权威账本。其他电脑的作业状态作为只追加共享事件写入 `print_records.db` 的 `RemotePrintJobEvents` 表，仅用于跨设备追溯，不参与本机打印执行裁决。

## 使用流程

1. 选择模板目录和 `.btw` 模板。
2. 在订单管理中添加客户、机型、颜色和订单号，并绑定一个或多个模板。
3. 配置模板数据源、锁定值、增降序、长度、校验文件、打印机和份数。
4. 回到打印页面选择完整订单与模板。
5. 扫码或输入字段，最后一项回车后提交打印。
6. 在历史记录中搜索、导入、导出或执行受控补打印。
7. 在 `同步中心` 创建协作空间并导出 `.btpsync`，或在成员电脑导入连接文件；通过 `立即同步` 查看队列、设备、冲突、用量和诊断。

## 数据目录

运行数据保存在 `%LOCALAPPDATA%\BarTenderPrinter`：

- `config.ini`：应用配置。
- `orders.json`：订单与模板绑定。
- `print_records.db`：打印历史。
- `print_jobs.db`：打印作业可靠性账本。
- `print_records.jsonl`：历史兼容副本。
- `history-records`：逐条历史归档。
- `previews`：预览缓存。
- `sync-profile.dat`：Windows DPAPI CurrentUser 保护的 WebDAV 凭据、空间信息和数据密钥。
- `sync.db`：同步 outbox、设备游标、冲突、设备端点、用量和活动记录。
- `template-cache`：摘要校验后的同步模板缓存。
- `sync-incoming`、`sync-staging`：同步接收与暂存目录。
- `direct-sync-certificates`：DPAPI 保护的本机直连证书。

坚果云远端只保存密文，根目录为 `BarTenderPrinterSync/spaces/{space-id}/`，包含 `space.enc`、`devices/`、`events/`、`templates/` 和预留的 `snapshots/`。当前实现使用不可变事件、outbox 和游标增量同步；周期快照、完整退避重试和损坏对象隔离仍在完善。

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

同步功能测试包含在 Windows 测试项目中，覆盖加密格式、连接文件、WebDAV、outbox/游标/冲突、数据适配、事件编排、端点、TLS 直连、回退和同步中心布局。

## 同步安全

- 仅接受坚果云规范 HTTPS DAV 地址；账号应使用坚果云应用密码。
- `.btpsync` 使用共享密码经 PBKDF2-HMAC-SHA256 600,000 次派生后加密，文件中不保存共享密码。请通过独立安全渠道传递文件与共享密码。
- 同步对象使用 32 字节数据密钥、随机 12 字节 nonce 和 AES-256-GCM v2；业务字段、模板内容和凭据不以明文上传。
- 直连仅尝试 WebDAV 发布且未过期的 host、端口和证书指纹，不扫描网络；请仅在受控局域网、企业专网或 VPN 中启用并配置 Windows 防火墙。
- 导出的脱敏诊断包含主机名、队列计数、用量和截断标识，不包含密码、数据密钥或业务字段值。

## 下载

前往 [Releases](https://github.com/tall-1997/Label-Printer/releases) 下载安装包。

## 许可

MIT License
