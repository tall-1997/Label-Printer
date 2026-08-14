# 打印历史业务 CSV 导出需求

## Introduction

本功能将当前模板的打印历史转换为面向业务使用的 CSV 文件。导出过程只读取历史记录、订单信息和当前模板数据源，不修改数据库及其他历史存储。

## Glossary

- **业务 CSV**：按订单业务字段和模板数据源展开的导出文件。
- **当前模板字段**：当前选中模板声明的数据源名称，按模板字段顺序排列。
- **订单业务信息**：订单中的客户、机型、颜色和订单号。
- **未映射值**：历史记录中不存在对应数据源名称的值。

## Requirements

### Requirement 1: 表头与字段顺序

**User Story:** AS 生产记录使用者, I want 固定业务字段和动态数据源列, so that WPS 或 Excel 可以直接分析打印历史。

#### Acceptance Criteria

1. WHEN 系统导出业务 CSV, 系统 SHALL 按“日期、客户、颜色、机型、订单号、当前模板字段、操作人、打印时间、打印状态”的顺序输出表头。
2. WHEN 当前模板包含多个数据源, 系统 SHALL 按当前模板字段顺序输出数据源详细名称。
3. WHEN 当前模板字段包含重复名称, 系统 SHALL 按不区分大小写的规则保留首次出现的名称。

### Requirement 2: 历史与订单字段映射

**User Story:** AS 生产记录使用者, I want 每条打印历史映射到订单和数据源列, so that 导出内容具有完整业务含义。

#### Acceptance Criteria

1. WHEN 历史记录包含订单标识, 系统 SHALL 从对应订单读取客户、颜色、机型和订单号。
2. WHEN 历史记录包含打印时间, 系统 SHALL 将“日期”格式化为 `yyyy-MM-dd`。
3. WHEN 历史记录包含打印时间, 系统 SHALL 将“打印时间”格式化为 `yyyy/MM/dd HH:mm:ss`。
4. WHEN 历史记录包含当前模板字段值, 系统 SHALL 按数据源名称映射到对应列。
5. WHEN 历史记录缺少订单、时间或数据源值, 系统 SHALL 为对应单元格输出空字符串。
6. WHEN 历史记录包含操作人和打印状态, 系统 SHALL 分别输出操作员账号和原始打印状态。

### Requirement 3: 文件拆分与命名

**User Story:** AS 生产记录使用者, I want 每个订单获得独立 CSV, so that 文件可以按订单归档。

#### Acceptance Criteria

1. WHEN 待导出记录包含多个订单, 系统 SHALL 按订单标识拆分业务 CSV。
2. WHEN 系统生成订单业务 CSV, 系统 SHALL 使用 `客户_机型_颜色_订单号_YYYYMMDD.csv` 作为文件名。
3. IF 文件名业务字段包含文件系统非法字符, 系统 SHALL 将非法字符转换为下划线。
4. IF 目标目录存在同名文件, 系统 SHALL 在获得操作员覆盖确认后写入导出结果。

### Requirement 4: CSV 兼容性与存储隔离

**User Story:** AS WPS 或 Excel 用户, I want 中文和数据源内容可靠显示, so that 导出文件可以直接打开使用。

#### Acceptance Criteria

1. WHEN 系统写出业务 CSV, 系统 SHALL 使用带 BOM 的 UTF-8 编码。
2. WHEN 系统写出数据源单元格, 系统 SHALL 使用双引号包裹内容并将内容中的双引号转义为两个双引号。
3. WHEN 系统写出 CSV 字段, 系统 SHALL 保持逗号、换行和双引号内容可被标准 CSV 解析器还原。
4. WHEN 系统执行导出, 系统 SHALL 仅读取历史记录、订单信息和模板字段。
5. WHEN 系统完成或取消导出, 数据库、JSONL、内部历史 CSV 和独立历史副本 SHALL 保持原有内容。
