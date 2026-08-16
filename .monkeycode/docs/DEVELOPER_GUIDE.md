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

测试覆盖配置模型、订单、数据校验、打印工作流、历史完整性、SQLite 账本、幂等和补打印规则。

## 发布

```powershell
dotnet publish BarTenderPrinter/BarTenderPrinter.csproj -c Release -r win-x64 --self-contained true -o publish
./scripts/Test-ReleaseArtifact.ps1 -PublishDirectory publish
```

发布目录必须包含 `BarTenderPrinter.exe` 和 `BarTenderPreviewHost.exe`。校验脚本拒绝证书、私钥、数据库、SQLite、日志、HASP、Sentinel、SafeNet、Hardlock、Dongle 和旧 MobileMes 资产。

Inno Setup 使用 `installer/BarTenderPrinter.iss` 生成当前用户安装包。CI 串行执行跨平台打印测试、Windows 测试、自包含发布、制品扫描和安装包构建。

## 开发约束

- 外部打印提交使用稳定幂等键和不可变请求快照。
- `Submitting` 状态恢复为 `Uncertain`，由人工核查实际打印结果。
- 补打印保持账户权限、审批、原因、原作业和序号链路。
- 历史格式变更保留旧校验和读取兼容。
- 发布制品保持无数据库、日志和敏感配置。
- 测试、构建和发布串行执行，避免共享输出目录冲突。
