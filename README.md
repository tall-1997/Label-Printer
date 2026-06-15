# Label Printer

BarTender 标签打印工具，集成 Seagull BarTender SDK，支持 .btw 模板自动化打印。

## 最新版本

**[v4.2.0-csharp](https://github.com/tall-1997/Label-Printer/releases/tag/v4.2.0-csharp)** - C# WinForms 原生版（推荐）

## 功能特性

### 核心功能
- **模板管理**：选择模板目录，下拉框切换 .btw 模板，异步生成模板预览图
- **数据源自动检测**：自动读取 .btw 模板内的命名子字符串字段，自由勾选需要的数据源
- **动态输入框**：根据数据源配置自动生成输入框，回车跳转下一个，最后一项回车触发打印
- **打印完成后自动清空**：清空所有输入框，焦点移回第一项，支持连续扫码打印

### 数据源校验
- **重复检测**：打印前检查所有数据源值是否已打印过，弹窗显示具体重复字段
- **用户确认**：可选择继续打印或取消

### 配置管理
- **保存/加载配置**：INI 格式保存，打印机、打印份数、数据源配置、模板目录
- **主界面直接操作**：打印机下拉框、打印份数选择框，选择后自动保存

### 历史记录
- **搜索**：按 IMEI、打印时间、状态搜索
- **导出**：导出为 CSV 文件
- **统计**：今日打印数、总打印数

### 日志
- **运行日志**：所有操作自动记录到日志文件
- **导出日志**：可导出为 .log 文件方便调试
- **清空日志**：一键清空界面日志

## 技术方案

| 项目 | 说明 |
|------|------|
| 语言 | C# (.NET Framework 4.8) |
| UI | WinForms + MIUIX 风格配色 |
| BarTender SDK | 反射调用 `Seagull.BarTender.Print`，无编译时 DLL 依赖 |
| 打印方式 | `Engine.Documents.Open` → `SubStrings[field].Value` → `doc.Print()` |
| 预览方式 | `doc.ExportImageToFile()` 异步导出临时 PNG |
| 配置存储 | Windows INI 文件（`kernel32.WritePrivateProfileString`） |
| 历史记录 | CSV 文件（`utf-8-sig` 编码） |

## 界面布局

```
┌──────────────────────────────────────────┐
│ BarTender 标签打印工具 v4.2.0    [导出日志]  │
│ [保存配置] [加载配置] [编辑数据源]            │
│                                            │
│ 模板目录：[D:\templates]        [浏览]      │
│ [模板下拉框]                   [预览图]     │
│                                            │
│ 打印机：[HP LaserJet        ▼] [刷新]      │
│                                    份数：[1]│
│ ┌──────────────────────────────────────┐  │
│ │ IMEI1：[____________________________] │  │
│ │ DS1：  [____________________________] │  │
│ └──────────────────────────────────────┘  │
│ [打印]                                     │
│ ┌─ 历史记录 ──┐  ┌─ 统计 ──┐               │
│ │ 搜索/导出    │  │ 今日/总  │               │
│ └─────────────┘  └────────┘               │
│ ┌─ 日志 ─────────────────────────────┐   │
│ └──────────────────────────────────────┘  │
│ 就绪 | 今日: 5 | 总计: 128                 │
└──────────────────────────────────────────┘
```

## 使用流程

1. 选择模板目录 → 下拉框选择 .btw 模板
2. 自动弹出数据源选择 → 勾选需要的字段，设置显示名称
3. 选择打印机（主界面下拉框）
4. 设置打印份数（主界面数字框）
5. 在输入框中扫码或输入数据 → 回车跳转下一个
6. 最后一项回车自动打印 → 打印完成后清空输入

## 环境要求

- Windows 10 或更高版本
- .NET Framework 4.8
- BarTender 2022 R2 Enterprise（Automation/Enterprise Automation 版）
- BarTender 需已正确注册并可启动

## 下载

前往 [Releases](https://github.com/tall-1997/Label-Printer/releases) 页面下载最新版本。

| 版本 | 说明 | 下载 |
|------|------|------|
| v4.2.0 | C# 原生版，主界面直接操作 | [下载](https://github.com/tall-1997/Label-Printer/releases/download/v4.2.0-csharp/BarTenderPrinter.exe) |
| v2.6.5 | Python 版（需 Python 环境） | [下载](https://github.com/tall-1997/Label-Printer/releases/download/v2.6.5/bartender-printer.exe) |

## 项目结构

```
Label-Printer/
├── BarTenderPrinter/          # C# WinForms 项目
│   ├── BarTenderPrinter.csproj
│   ├── Program.cs
│   ├── MainForm.cs            # 主窗体逻辑
│   ├── MainForm.Designer.cs   # 主窗体 UI 定义
│   ├── BarTenderService.cs    # BarTender SDK 反射调用
│   ├── HistoryManager.cs      # 历史记录管理
│   ├── LoggerService.cs       # 日志服务
│   └── MiuiTheme.cs           # MIUIX 风格主题
├── bartender_printer.py       # Python 版（v2.x）
├── label_printer.py           # 通用标签打印工具
└── .github/workflows/         # GitHub Actions 自动构建
```

## 开发

### C# 版
```bash
# 使用 Visual Studio 打开
BarTenderPrinter/BarTenderPrinter.csproj

# 或使用 MSBuild 命令行
msbuild BarTenderPrinter/BarTenderPrinter.csproj /p:Configuration=Release
```

### Python 版
```bash
pip install pyinstaller pywin32 openpyxl
python bartender_printer.py
```

## 许可证

MIT License
