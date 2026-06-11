# Label Printer

标签打印工具集合，支持 BarTender 2021 Automation 和通用标签打印。

## 工具列表

### 1. BarTender 标签打印工具 v2.0
- 集成 BarTender 2021 Automation
- 支持 IMEI 标签打印
- Excel/CSV 存储打印记录
- 多标签页界面（打印+历史+统计）
- 打印前校验是否已打印

### 2. 通用标签打印工具 v1.1
- 自定义标签模板
- 快速打印和批量打印
- 数据校验（防止重复打印）
- 支持 JSON/CSV 数据导入

## 下载

前往 [Releases](https://github.com/tall-1997/Label-Printer/releases) 页面下载最新版本。

## 使用要求

### BarTender 标签打印工具
- Windows 10 或更高版本
- BarTender 2021 Automation 版已安装
- 专用标签打印机

### 通用标签打印工具
- Windows 10 或更高版本
- 支持任何打印机

## 开发

### 依赖
- Python 3.8+
- pywin32
- openpyxl (可选，用于 Excel 导出)

### 运行
```bash
python bartender_printer.py
# 或
python label_printer.py
```

### 打包
```bash
# Windows
build_windows.bat
```

## 许可证

MIT License
