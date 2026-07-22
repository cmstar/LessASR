# LessASR Soft Fluent UI 实施计划

日期：2026-07-22

关联设计：`docs/superpowers/specs/2026-07-22-lessasr-soft-fluent-ui-design.md`

## Task 1：可测试的展示模型

1. 为 `StatsViewModel` 的今日指标、30 天汇总和 7 天趋势编写失败测试。
2. 为 `HistoryViewModel` 的今天 / 昨天 / 更早分组编写失败测试。
3. 实现聚合与分组，并运行 App 测试项目。

## Task 2：主题与基础组件

1. 引入固定版本的 WPF UI 包。
2. 新建 `Themes/Colors.xaml`、`Themes/Typography.xaml` 与 `Themes/Controls.xaml`。
3. 建立卡片、导航按钮、主要/次要按钮、键帽、状态徽标、输入控件与列表行样式。
4. 在 `App.xaml` 合并主题资源。
5. 构建 App 项目，确认资源键与 WPF UI 版本兼容。

## Task 3：主窗口壳与导航

1. 为 `MainViewModel` 增加可测试的导航状态和导航命令。
2. 用 Fluent Window、左侧导航和内容区域替换顶部 `TabControl`。
3. 保留设置页切入时清理保存反馈的行为。
4. 验证关闭到托盘、最小化和托盘恢复行为。

## Task 4：首页、历史与统计

1. 实现首页状态引导、快捷键和今日指标。
2. 实现 7 天轻量趋势视图。
3. 用日期分组列表替换历史 `DataGrid`。
4. 用 KPI、趋势和每日明细替换统计 `DataGrid`。
5. 验证空数据、长文本与 50 条历史记录。

## Task 5：模型、设置与诊断

1. 将模型页改为信息卡与操作卡。
2. 将设置页拆为识别、模型服务、数据隐私和应用行为卡片。
3. 将 Debug 页重命名为诊断并保留全部模拟命令。
4. 验证设置保存、模型操作和错误信息显示。

## Task 6：听写浮窗与连续听写窗口

1. 将公共颜色和控件样式应用于浮窗。
2. 重做浮窗信息层级与状态操作区，不改变焦点保护逻辑。
3. 重做连续听写分段卡片、提示与底部操作栏。
4. 验证失败段、长文本、自动增长与滚轮转发。

## Task 7：验证与收尾

1. 运行 `dotnet test LocalAsrClient.sln`。
2. 运行 `dotnet build LocalAsrClient.sln`。
3. 使用 `--test-mode` 人工检查主窗口、听写浮窗与连续听写窗口。
4. 检查 100%、125%、150% DPI 和最小窗口尺寸。
5. 修复视觉与可访问性问题，更新相关文档。
