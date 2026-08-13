# Requirements Document

## Introduction

打印预览功能在主界面右侧展示当前模板最近一次成功打印的标签效果，并支持操作员按需开启或关闭。

## Glossary

- **预览窗**：停靠在主界面右侧的无边框图片窗口。
- **成功打印记录**：状态为 `PASS` 或 `REPRINT_PASS` 的历史记录。
- **模板预览**：使用当前模板且不填充历史字段生成的图片。

## Requirements

### Requirement 1

**User Story:** AS 操作员, I want 在打印页面开启预览, so that 我可以持续查看标签效果。

#### Acceptance Criteria

1. WHEN 操作员开启预览控件, 系统 SHALL 在主界面右侧显示预览窗。
2. WHILE 预览窗显示, 系统 SHALL 跟随主界面移动和缩放。
3. WHEN 操作员关闭预览窗, 系统 SHALL 同步关闭预览控件。

### Requirement 2

**User Story:** AS 操作员, I want 查看最近成功打印的标签, so that 预览内容与生产数据一致。

#### Acceptance Criteria

1. WHEN 当前模板存在成功打印记录, 系统 SHALL 使用最近成功记录的字段值生成预览图。
2. IF 当前模板缺少成功打印记录, 系统 SHALL 使用原模板生成预览图。
3. WHEN 当前模板发生变化, 系统 SHALL 刷新预览内容。
4. WHEN 新打印作业成功提交, 系统 SHALL 使用该作业字段值刷新预览内容。

### Requirement 3

**User Story:** AS 操作员, I want 预览不影响连续打印, so that 扫码操作保持连续。

#### Acceptance Criteria

1. WHEN 系统生成预览图, 系统 SHALL 使用目标 BarTender 安装附带的官方 `Seagull.BarTender.Print` .NET SDK。
2. WHILE 打印队列处理作业, 系统 SHALL 保持 BarTender COM 调用在专用 STA 线程串行执行。
3. WHEN 预览导出失败, 系统 SHALL 保留打印流程并在预览窗显示错误状态。
4. WHEN 系统生成预览图, 系统 SHALL 保持 BarTender 窗口隐藏且不得访问剪贴板或提交打印作业。
5. BEFORE 系统启用预览入口, 系统 SHALL 在安装目标 BarTender 版本的 Windows 机器上完成静默导出和打印隔离验证。
