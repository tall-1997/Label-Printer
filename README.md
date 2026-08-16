# BarTender Printer

基于 .NET 8、PostgreSQL 16 和 Seagull BarTender COM 接口的包装 MES 客户端与中心服务。系统覆盖订单状态、生产主数据、号码生命周期、组装包装、称重与写号任务、四类标签自动作业、质量处置、返工、出库、归档修复、CSV 数据交换、追溯及恢复操作。

![界面预览](assets/preview.png)

## 最新版本

**v5.7.91** - C# WinForms 完整 MES 工作流版

## 功能特性

### 核心功能
- **完整 MIUIX WinForms 页面**：打印、订单管理和 MES 工位统一使用 MIUIX 风格主题与响应式页面结构
- **订单状态闭环**：支持草稿、发布、生产、暂停、恢复和关闭，并以乐观并发版本保护状态转换
- **生产主数据**：维护生产单元、标准/返工路线、有序工序、工位资格和四级包装单元
- **号码生命周期**：支持 IMEI、SN、PSN、MSN、卡通箱号和卡板号的保留、分配、冻结、释放、报废及历史查询
- **四类标签自动作业**：机身创建及彩盒、卡通箱、卡板满容量关闭时自动生成中心打印作业
- **称重与写号任务**：按订单/包装类型执行重量规则判定，支持写号任务创建、领取、结果回传和不确定状态冻结
- **质量与返工**：支持检验批、检验结果、质量处置任务、包装/生产单元冻结、放行、返工和报废
- **出库与归档**：支持出库单、卡通箱扫描、数量确认、不可变归档、摘要校验、修复任务和替代归档
- **CSV 导入导出**：订单和号段分批校验后原子导入，导出订单、号段和追溯数据并按权限脱敏
- **操作恢复**：保存在线校验意图与打印快照，支持原幂等键重新提交、同步核对和转人工处理
- **订单管理页面**：按客户、机型、颜色、订单号选择包装订单并调用绑定配置
- **添加订单**：新增订单时填写可复用客户、机型、颜色和唯一订单号，页面内平铺配置模板和数据源
- **多模板订单**：一个订单可绑定多个模板，每个模板独立保存数据源、打印机、份数、锁定和增降序设置
- **模板绝对路径**：订单直接引用操作员选择的 .btw 模板完整路径，编辑与打印读取同一模板文件
- **模板更新检测**：切换订单模板或进入打印页面时核对外部模板，更新后由操作员选择外部新版或当前归档版
- **模板管理**：选择模板目录，下拉框切换 .btw 模板，异步生成模板预览图
- **右键打开模板**：安装后可在 `.btw` 文件右键菜单选择“使用 BarTenderPrinter 打开”并自动加载模板
- **数据源自动检测**：自动读取 .btw 模板内的命名子字符串字段，自由勾选、拖拽排序并设置长度规则
- **动态输入框**：根据数据源配置自动生成输入框，右侧锁图标支持固定锁定和增降序锁定
- **打印完成后自动清空**：清空非增序字段，增序字段自动更新并锁定
- **连续静默打印**：扫码作业进入本地 FIFO 队列后立即恢复输入，后台按顺序提交并短暂重试 BarTender Busy 错误
- **右侧标签预览框架**：通过独立的 .NET Framework 4.8 x64 宿主调用 BarTender 2022 R2 SDK 导出 PNG
- **SDK 运行时隔离**：主应用保持 .NET 8，预览宿主负责 Engine 启停、动态字段赋值、超时回收和错误隔离
- **预览渲染缓存**：模板内容哈希和字段快照未变化时复用已验证 PNG，减少 Engine 与模板重复调用
- **动态字段兼容**：打印后预览按当前模板字段过滤历史旧字段，字段无交集时回退原模板缩略图
- **预览自适应**：预览窗口根据标签图片比例和当前屏幕工作区自动调整尺寸

### 数据源增序功能
- **自动增序/降序**：支持设置步长（+1 增序，-1 降序）
- **智能识别**：自动识别数字部分，如 `AC20260616` → `AC20260617`
- **保留前导零**：`001` → `002`
- **待打印值恢复**：历史导入或补打印旧编号后自动恢复到已保存的下一条待打印编号
- **锁定图标**：输入完成后点击锁图标，普通字段固定锁定，增降序字段按增降序锁定

### 数据校验
- **重复检测**：打印前检查所有数据源值是否已打印过，弹窗显示具体重复字段
- **本地数据校验**：加载 CSV/Excel/TXT 文件作为模板校验快照，支持选择列并逐数据源决定是否参与校验
- **校验开关**：可勾选是否启用本地数据校验

### 配置管理
- **保存/加载配置**：INI 和模板级 JSON 保存打印机、打印份数、数据源配置、模板目录
- **模板级配置**：订单内各模板独立保存数据源顺序、锁定值、增降序待打印值、长度规则、打印机和份数
- **最近状态恢复**：正常退出时保存最近订单、模板、打印机、份数和预览开关，启动后自动恢复工作上下文
- **静默升级兼容**：旧版数据源配置首次加载时按当前模板字段自动迁移，启动和切换模板不再弹出旧数据源窗口
- **独立历史副本**：每条打印历史按日期和记录 ID 保存到程序目录 `history-records`，便于审计追溯
- **主界面直接操作**：打印机下拉框、打印份数选择框、校验数据开关、长度校验开关

### 历史记录
- **搜索**：按字段名、字段值、模板、时间、状态、打印机和份数搜索
- **导入**：从历史记录选择部分字段导入到当前输入框
- **补打印**：使用历史字段快照补打印，并可选择本次打印机
- **补打印独立策略**：补打印保留账户审批和模板版本确认，直接使用历史字段快照提交，不执行普通打印校验
- **排除**：历史记录支持右键将单条记录排除出界面、重复校验和业务检测，原始数据持续保留
- **清空控件**：高风险确认后批量排除当前模板活动历史，数据库、JSONL 和独立副本保持完整
- **导出**：按当前模板和搜索结果导出为 CSV 文件
- **统计**：今日打印数、总打印数

### 日志
- **运行日志**：所有操作自动记录到日志文件
- **导出日志**：可导出为 .log 文件方便调试
- **清空日志**：一键清空界面日志

### 离线模式
- 无 BarTender 时自动进入离线模式
- 所有非打印功能正常可用
- 手动配置数据源

### 设备与授权边界
- 首期设备实现为模拟电子称和模拟写号适配器
- `IScaleAdapter` 与 `IIdentifierWriter` 为真实设备适配接口，厂商协议和真实硬件工具由后续适配器接入
- 项目范围排除 HASP/Sentinel 商业授权、加密狗驱动和许可证服务器模块
- 登录认证、PBKDF2-SHA256 密码存储、Bearer 工位会话、角色授权、审计链和敏感字段脱敏继续作为安全基础能力

## 技术方案

| 项目 | 说明 |
|------|------|
| 语言 | C# (.NET 8.0) |
| UI | WinForms + MIUIX 风格配色 |
| 中心服务 | ASP.NET Core Minimal API |
| 中心存储 | PostgreSQL 16，v1-v17 版本化迁移 |
| 设备 | 模拟适配器 + 真实适配接口 |
| BarTender | COM 接口调用 |
| 打印方式 | `Formats.Open` → `SetNamedSubStringValue` → `PrintOut` |
| 预览方式 | .NET Framework 4.8 x64 隔离宿主调用 BarTender 2022 R2 `Seagull.BarTender.Print` SDK |
| 配置存储 | Windows INI 文件 |
| 本地历史 | SQLite、JSONL 兼容备份和 CSV 导出 |
| 发布方式 | Inno Setup 当前用户安装包（内置 .NET 运行时） |

## 界面布局

```
┌──────────────────────────────────────────┐
│ BarTender Printer v5.7.91  By---池鱼  [日志] [关于] [导出日志] │
│ [保存配置] [加载配置] [编辑数据源]            │
│ [加载校验数据] [✓启用校验] 已加载: N条       │
│                                            │
│ 模板目录：[D:\templates]        [浏览]      │
│ [模板下拉框]                    [预览图]    │
│                                            │
│ 打印机：[HP LaserJet        ▼] [刷新]      │
│                                    份数：[1]│
│ ┌──────────────────────────────────────┐  │
│ │ IMEI1：[________________]            │  │
│ │ 箱号：  [AC20260616]  (锁定+增序)    │  │
│ └──────────────────────────────────────┘  │
│ [打印]                                     │
│ ┌─历史记录/统计──────────────────────┐    │
│ └──────────────────────────────────────┘  │
│ ┌─日志────────────────────────────────┐  │
│ └──────────────────────────────────────┘  │
│ 就绪 | 今日: 5 | 总计: 128                 │
└──────────────────────────────────────────┘
```

## 使用流程

1. 选择模板目录 → 下拉框选择 .btw 模板
2. 点击“添加订单” → 填写客户、机型、颜色、订单号并添加一个或多个模板
3. 切换订单模板，在页面内配置各模板的数据源、锁定、增降序、长度、打印机和份数
4. 在“订单管理”页面选择客户、机型、颜色、订单号
5. 选择打印机（主界面下拉框）
6. 设置打印份数（主界面数字框）
7. 输入增序字段初始值（如箱号=AC20260616）
8. 在输入框中扫码/输入数据 → 回车跳转下一个
9. 最后一项回车自动打印 → 增序字段更新待打印值，其他字段清空
10. 继续扫码输入，重复打印
11. 需要复用历史数据时，在历史记录中选择记录并点击“导入”或“补打印选中项”
12. 需要直接打开模板时，右键 `.btw` 文件选择“使用 BarTenderPrinter 打开”
13. MES 业务从 `MES 工位` 的订单、主数据、生产工位、质量返工、出库归档、导入导出、作业与恢复七个分组执行
14. 称重与写号页当前明确标注模拟模式；现场真实设备接入前需实现并验证对应适配器
15. 待处理操作在恢复页按原幂等键重新提交，状态冲突转人工核查

> Windows 11 可能需要先点击“显示更多选项”，再选择“使用 BarTenderPrinter 打开”。

## 环境要求

- Windows 10/11 x64
- BarTender 2022 R2 Enterprise（Automation/Enterprise Automation 版）
- PostgreSQL 16（运行中心 MES API 时）
- 安装包内置 .NET 运行时

## 跨平台测试

当前已确认结果：Domain 43、Devices 36、Printing 7、MesClient 32、Persistence 26、MesApi 34，共 178 项跨平台测试通过。Domain 测试覆盖基础契约、订单、生产、号段、包装、称重、质量、返工、出库和归档。

## 下载

前往 [Releases](https://github.com/tall-1997/Label-Printer/releases) 页面下载最新版本。

| 版本 | 大小 | 说明 |
|------|------|------|
| [v5.7.91](https://github.com/tall-1997/Label-Printer/releases/tag/v5.7.91) | ~50 MB | 完整 MES 工作流、PostgreSQL v17、CSV 交换、工位隔离与安全加固版 |
| [v5.7.88](https://github.com/tall-1997/Label-Printer/releases/tag/v5.7.88) | ~50 MB | MES 工位、设备模拟、质量返工、出库归档与中心追溯集成版 |
| [v5.7.87](https://github.com/tall-1997/Label-Printer/releases/tag/v5.7.87) | ~50 MB | 打印协调、历史灾难恢复与并发可靠性增强版 |
| [v5.7.83](https://github.com/tall-1997/Label-Printer/releases/tag/v5.7.83) | ~50 MB | 响应式布局、多 DPI 与待核查状态显示修复版 |
| [v5.7.82](https://github.com/tall-1997/Label-Printer/releases/tag/v5.7.82) | ~50 MB | 打印可靠性与审计加固版 |
| [v5.7.81](https://github.com/tall-1997/Label-Printer/releases/tag/v5.7.81) | ~50 MB | 超级管理员固定凭据版 |
| [v5.7.80](https://github.com/tall-1997/Label-Printer/releases/tag/v5.7.80) | ~50 MB | 账户、补打印、历史完整性与文件可靠性增强版 |
| [v5.7.74](https://github.com/tall-1997/Label-Printer/releases/tag/v5.7.74) | ~50 MB | 顶栏侧栏、补打印校验、订单级联与按钮显示修复版 |
| [v5.7.73](https://github.com/tall-1997/Label-Printer/releases/tag/v5.7.73) | ~50 MB | 零占位侧栏、订单页空间回收与预览运行时兼容修复版 |
| [v5.7.72](https://github.com/tall-1997/Label-Printer/releases/tag/v5.7.72) | ~50 MB | 多 DPI 控件可见性、表格可读性与预览避让优化版 |
| [v5.7.71](https://github.com/tall-1997/Label-Printer/releases/tag/v5.7.71) | ~50 MB | 订单业务打印历史 CSV 导出版 |
| [v5.7.70](https://github.com/tall-1997/Label-Printer/releases/tag/v5.7.70) | ~50 MB | 浅色导航、响应式布局与多 DPI 显示修复版 |
| [v5.7.69](https://github.com/tall-1997/Label-Printer/releases/tag/v5.7.69) | ~50 MB | 现代专业界面、矢量图标、高 DPI 与日志折叠版 |
| [v5.7.68](https://github.com/tall-1997/Label-Printer/releases/tag/v5.7.68) | ~50 MB | 旧版静默迁移、非破坏历史排除与独立历史副本版 |
| [v5.7.67](https://github.com/tall-1997/Label-Printer/releases/tag/v5.7.67) | ~50 MB | 动态预览、逐字段校验与最近状态恢复优化版 |
| [v5.7.66](https://github.com/tall-1997/Label-Printer/releases/tag/v5.7.66) | ~49 MB | SDK 发现、Engine 生命周期与预览缓存优化版 |
| [v5.7.65](https://github.com/tall-1997/Label-Printer/releases/tag/v5.7.65) | ~49 MB | 原模板缩略图与动态字段预览分流版 |
| [v5.7.64](https://github.com/tall-1997/Label-Printer/releases/tag/v5.7.64) | ~49 MB | BarTender 2022 R2 官方 SDK 静默预览候选版 |
| [v5.7.63](https://github.com/tall-1997/Label-Printer/releases/tag/v5.7.63) | ~49 MB | 右侧停靠标签预览框架版；已发布版本中的图片导出尚未通过运行时验证 |
| [v5.7.62](https://github.com/tall-1997/Label-Printer/releases/tag/v5.7.62) | ~49 MB | FIFO 连续打印可靠性增强版 |
| [v5.7.61](https://github.com/tall-1997/Label-Printer/releases/tag/v5.7.61) | ~49 MB | 连续无等待静默打印版 |
| [v5.7.60](https://github.com/tall-1997/Label-Printer/releases/tag/v5.7.60) | ~49 MB | 静默打印和简化补打印版 |
| [v5.7.59](https://github.com/tall-1997/Label-Printer/releases/tag/v5.7.59) | ~49 MB | 订单号下拉和增序恢复修复版 |
| [v5.7.58](https://github.com/tall-1997/Label-Printer/releases/tag/v5.7.58) | ~49 MB | 订单级联和锁定步长修复版 |
| [v5.7.57](https://github.com/tall-1997/Label-Printer/releases/tag/v5.7.57) | ~49 MB | 订单管理交互和账户登录修复版 |
| [v5.7.56](https://github.com/tall-1997/Label-Printer/releases/tag/v5.7.56) | ~49 MB | SQLite 迁移闭环与审计强化版 |
| [v5.7.55](https://github.com/tall-1997/Label-Printer/releases/tag/v5.7.55) | ~49 MB | SQLite 历史主存储与审计增强版 |
| [v5.7.54](https://github.com/tall-1997/Label-Printer/releases/tag/v5.7.54) | ~49 MB | JSONL 历史反序列化修复版 |
| [v5.7.53](https://github.com/tall-1997/Label-Printer/releases/tag/v5.7.53) | ~49 MB | 历史可靠性和测试隔离增强版 |
| [v5.7.52](https://github.com/tall-1997/Label-Printer/releases/tag/v5.7.52) | ~49 MB | UI 交互优化与测试保障版 |
| [v5.7.51](https://github.com/tall-1997/Label-Printer/releases/tag/v5.7.51) | ~49 MB | 数据模型升级与 JSONL 历史版 |
| [v5.7.50](https://github.com/tall-1997/Label-Printer/releases/tag/v5.7.50) | ~49 MB | 架构重构基础设施版 |
| [v5.7.49](https://github.com/tall-1997/Label-Printer/releases/tag/v5.7.49) | ~49 MB | P2 体验与审计增强版 |
| [v5.7.48](https://github.com/tall-1997/Label-Printer/releases/tag/v5.7.48) | ~49 MB | P1 数据校验与历史稳定性修复版 |
| [v5.7.47](https://github.com/tall-1997/Label-Printer/releases/tag/v5.7.47) | ~49 MB | P0 打印安全校验修复版 |
| [v5.7.45](https://github.com/tall-1997/Label-Printer/releases/tag/v5.7.45) | ~49 MB | 数据源表格批量选择和步长合并版 |
| [v5.7.44](https://github.com/tall-1997/Label-Printer/releases/tag/v5.7.44) | ~49 MB | 打印页订单级联选择和锁定图标优化版 |
| [v5.7.43](https://github.com/tall-1997/Label-Printer/releases/tag/v5.7.43) | ~49 MB | 订单草稿和打印回调编译修复版 |
| [v5.7.42](https://github.com/tall-1997/Label-Printer/releases/tag/v5.7.42) | ~49 MB | 订单草稿放弃和打印回调安全修复版 |
| [v5.7.41](https://github.com/tall-1997/Label-Printer/releases/tag/v5.7.41) | ~49 MB | BarTender STA 调用和历史一致性修复版 |
| [v5.7.40](https://github.com/tall-1997/Label-Printer/releases/tag/v5.7.40) | ~49 MB | 打印页布局重排和退出确认版 |
| [v5.7.39](https://github.com/tall-1997/Label-Printer/releases/tag/v5.7.39) | ~49 MB | 打印页只读配置和订单选择状态修复版 |
| [v5.7.38](https://github.com/tall-1997/Label-Printer/releases/tag/v5.7.38) | ~49 MB | 订单管理长度覆盖、未保存提示和锁定按钮版 |
| [v5.7.37](https://github.com/tall-1997/Label-Printer/releases/tag/v5.7.37) | ~49 MB | 订单管理数据源明细和长度校验修复版 |
| [v5.7.36](https://github.com/tall-1997/Label-Printer/releases/tag/v5.7.36) | ~49 MB | 订单模板校验数据持久化和内置侧栏图标版 |
| [v5.7.35](https://github.com/tall-1997/Label-Printer/releases/tag/v5.7.35) | ~49 MB | 侧边栏折叠、校验导入和重复校验性能优化版 |
| [v5.7.34](https://github.com/tall-1997/Label-Printer/releases/tag/v5.7.34) | ~49 MB | 模板绝对路径、历史导入补录和订单模板维护版 |
| [v5.7.33](https://github.com/tall-1997/Label-Printer/releases/tag/v5.7.33) | ~49 MB | 添加模板时数据源单元格崩溃修复版 |
| [v5.7.32](https://github.com/tall-1997/Label-Printer/releases/tag/v5.7.32) | ~49 MB | 订单编辑状态、布局冲突和界面可读性修复版 |
| [v5.7.31](https://github.com/tall-1997/Label-Printer/releases/tag/v5.7.31) | ~49 MB | 订单完整设置平铺编辑版 |
| [v5.7.30](https://github.com/tall-1997/Label-Printer/releases/tag/v5.7.30) | ~49 MB | 多模板订单、模板更新检测和页面布局修复版 |
| [v5.7.29](https://github.com/tall-1997/Label-Printer/releases/tag/v5.7.29) | ~49 MB | C# WinForms 当前用户安装版 |
| [v5.7.28](https://github.com/tall-1997/Label-Printer/releases/tag/v5.7.28) | ~49 MB | 订单添加页面内嵌数据源设置版 |
| [v5.7.27](https://github.com/tall-1997/Label-Printer/releases/tag/v5.7.27) | ~49 MB | 包装 MES 订单管理初版 |
| [v5.7.26](https://github.com/tall-1997/Label-Printer/releases/tag/v5.7.26) | ~49 MB | 打印流程健壮性修复版 |
| [v5.7.25](https://github.com/tall-1997/Label-Printer/releases/tag/v5.7.25) | ~49 MB | 历史右键和模板菜单修复版 |
| [v5.7.24](https://github.com/tall-1997/Label-Printer/releases/tag/v5.7.24) | ~49 MB | 右键打开模板和历史单条删除版 |
| [v5.7.23](https://github.com/tall-1997/Label-Printer/releases/tag/v5.7.23) | ~49 MB | 数据源锁定工作流优化版 |
| [v5.7.18](https://github.com/tall-1997/Label-Printer/releases/tag/v5.7.18) | ~155 MB | 历史自包含单文件版 |
| [v2.6.5](https://github.com/tall-1997/Label-Printer/releases/download/v2.6.5/bartender-printer.exe) | 38 KB | Python 版（需 Python 环境） |

## 项目结构

```
Label-Printer/
├── BarTenderPrinter/          # C# WinForms 项目
│   ├── BarTenderPrinter.csproj
│   ├── Program.cs
│   ├── MainForm.cs            # 主窗体逻辑
│   ├── MainForm.Designer.cs   # 主窗体 UI 定义
│   ├── BarTenderService.cs    # BarTender COM 调用
│   ├── HistoryManager.cs      # 历史记录管理
│   ├── TemplateSettingsManager.cs # 模板级设置管理
│   ├── OrderManager.cs        # 包装订单和模板归档管理
│   ├── LoggerService.cs       # 日志服务
│   └── MiuiTheme.cs           # MIUIX 风格主题
├── BarTenderPrinter.Domain/   # 跨平台领域模型与共享契约
├── BarTenderPrinter.Devices/  # 模拟设备适配器与真实设备接口
├── BarTenderPrinter.Persistence/ # PostgreSQL 16 持久化、迁移和 CSV 交换
├── BarTenderPrinter.MesApi/   # MES 中心 API、认证、授权和审计
├── BarTenderPrinter.*.Tests/  # 跨平台及 Windows 测试项目
├── bartender_printer.py       # Python 版（v2.x）
├── label_printer.py           # 通用标签打印工具
├── assets/                    # 资源文件
│   └── preview.png            # 界面预览图
└── .github/workflows/         # GitHub Actions 自动构建和测试
    ├── build-csharp.yml       # 构建工作流
    ├── test-csharp.yml        # 自动化测试
    └── test-core.yml          # 核心功能测试
```

## 开发

### C# 版
```bash
# 使用 Visual Studio 打开
BarTenderPrinter/BarTenderPrinter.csproj

# 恢复依赖并在 Windows 上运行测试
dotnet restore BarTenderPrinter.Tests/BarTenderPrinter.Tests.csproj
dotnet test BarTenderPrinter.Tests/BarTenderPrinter.Tests.csproj -c Release

# 在 Linux 上验证 Windows 目标项目编译
DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet build BarTenderPrinter.Tests/BarTenderPrinter.Tests.csproj -c Release -p:EnableWindowsTargeting=true

# 发布 Windows x64 自包含应用
dotnet publish BarTenderPrinter/BarTenderPrinter.csproj -c Release -r win-x64 --self-contained true -o publish

# 使用 Inno Setup 6 构建当前用户安装包
iscc installer/BarTenderPrinter.iss
```

MES API 通过 `ConnectionStrings__MesDatabase` 接收 PostgreSQL 16 连接字符串，通过 `MesSecurity__Sessions__{index}` 配置 Bearer 工位会话。配置模板只保留占位符，实际令牌和密码由部署环境注入。

### Python 版
```bash
pip install pyinstaller pywin32 openpyxl
python bartender_printer.py
```

## 许可证

MIT License
