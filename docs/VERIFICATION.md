# 验收结果：MySQL 网页 2.0.0

日期：2026-11-09。环境：Windows x64、.NET SDK 10.0.403、MySQL 8.4.13、Microsoft Edge。

## 隔离数据库与浏览器

最终结果见 [evidence/mysql-20261109/api-report.json](evidence/mysql-20261109/api-report.json) 与 [浏览器报告](evidence/mysql-20261109/browser-report.json)。

采用新版 C++ schema.sql 的测试副本，单独启动 MySQL：本机绑定、随机端口、独立数据目录。测试账号与记录只存在于隔离实例，测试后清理并关闭。

已验证 528 条整日记录的完整统计与 Excel；覆盖有效零、无效体积、无 RTK、匹配成功但测量失败、模板指标、未执行配准默认零、低指标遵从 C++、来源过滤、编号大小写、历史／当前名称、特殊字符、时间边界、快照一致性、TTL／预算／上限、认证、会话隔离、来源校验、并发读写、错误表结构和缺失配置。

界面覆盖六页，1366×768／1920×1080，首页 5 秒更新、分页、详情、缺失照片、周报预览与浏览器真实 Excel 下载。未捕获 JavaScript 异常为 0。原设计稿与新版顶部、侧栏、主内容、首页卡片尺寸一致。

截图中的数百条记录为测试数据，不是现场实测或正式库数据。

| 证据 | 内容 |
|---|---|
| before-reference-home.png | 原始 HTML 设计稿，作为界面改造前的视觉参照 |
| home-1366.png / home-1920.png | 新版首页 |
| query.png | 完整范围统计与分页 |
| detail-metrics.png | RTK、模板与配准指标 |
| detail-invalid.png | 无效体积、缺失照片 |
| export.png | 冻结查询的周报预览 |
| device.png / config.png / users.png / email.png | 保留布局，未接入功能明确展示 |
| setup.png / login.png | 初始化与登录 |

测试日志中 MySQL 1146 是主动临时改名测试表、验证 503 错误响应的预期故障，不是交付时的连接失败。

## 自包含包与正式库

自包含 win-x64 包在移除开发 SDK 环境／工具 PATH 后可独立启动，使用独立网页账号目录完成登录、首页、查询与 XLSX 导出。正式 MySQL 使用原有 mine_reader，只读权限已经核对。

本次正式库为空：首页 0、查询 0、Excel 正常生成。没有向正式库添加网页样本；现场精度、真实车辆测量、雷达链路不属于这次网页验收。

包验证结果保存到 .verification/package/report.json，正式部署时保留现有网页账号、Cookie 密钥和 appsettings.Local.json。

## 复测

在项目目录执行：

```bat
test-web.cmd -Browser
```

MySQL 安装路径不同时，先设置 MINE_TEST_MYSQL_BIN。发布包读库检查：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\check-package.ps1
```

该检查使用新的隔离网页登录账号，不会覆盖正式 admin／viewer。它会读取正式数据库，但不会写入测量表。


最终自动检查：60 项通过。正式部署记录见 evidence/mysql-20261109/deployment-report.json。C++ 49 个受检文件未改变，设计稿未改变；账号、Cookie 密钥和本机配置哈希相同。
