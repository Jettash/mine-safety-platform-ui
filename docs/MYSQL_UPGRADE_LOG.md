# MySQL 网页改造记录

实施日期：2026-11-09；交付版本：2.0.0。  
网页项目：`E:\BaiduNetdiskDownload\mine-safety-web`。  
C++ 对照：`E:\code\mine-safety-platform\mine-safety-platform`。

## 已完成的代码与数据改造

| 部分 | 修改文件 | 改动 | 验收结果 |
|---|---|---|---|
| 数据访问 | RecordRepository.cs、MineSafety.Web.csproj、packages.lock.json | SQLite 换为 MySqlConnector；只读事务与 SELECT；不迁移正式库 | 隔离库、正式库均通过 |
| 口径 | Models.cs、RecordRepository.cs | RTK_LIDAR、有效零、无效体积、历史车辆名称、GPS、模板配准 | 60 项接口及数据检查通过 |
| 快照 | QuerySnapshots.cs | 保留原有机制，适配新记录模型 | 翻页、详情、报表、过期、预算、会话隔离通过 |
| 首页 | index.html、app.js | 今日总量、有效、无效、有效体积；5 秒更新、最新 5 条 | 浏览器自动刷新通过 |
| 查询 | index.html、app.js | 编号／历史名称／当前名称；保留时间快捷项和分页 | 超过 500 条仍完整统计 |
| 详情 | index.html、app.js、app.css | RTK、速度、模板、RMSE、匹配率、覆盖率、GPS、算法版本、原始原因 | 真实 DOM 与截图通过 |
| 报表 | Reports.cs、app.js | 新统计和 22 列车辆明细；继续冻结范围 | Excel 与查询快照一致，浏览器下载通过 |
| 认证 | Program.cs、Accounts.cs | 原认证保留；数据库错误改为 MySQL 明确错误响应 | 原账号和 Cookie 密钥保留 |
| 配置 | appsettings.json、set-mysql-connection.ps1、configure-mysql.cmd | 8081；现有 SELECT 账号；DPAPI 加密连接文件 | 自包含包实际读库通过 |
| 运维 | build.ps1、check-package.ps1、allow-lan.ps1 | 2.0.0 包、MySQL 验收、默认端口 8081 | 已部署并运行 |
| 测试 | MySqlSandbox.cs、Fixture.cs、Program.cs、BrowserQa.cs、mysql-schema.sql | 独立实例、随机端口、真实 MySQL 表结构与 Edge 验收 | 不向正式库添加测试记录 |

表内 C# 源码位于 src/MineSafety.Web；页面位于该目录的 wwwroot；脚本位于 scripts；测试位于 tests/MineSafety.Web.Tests。

## 界面变化逐项记录

| 原界面 | 新界面 | 原因 |
|---|---|---|
| 今日正常／报警／待复核 | 有效测量／无效测量／有效体积 | 新库没有安全报警类别，不能混淆算法无效与报警 |
| 车牌号查询 | 车辆编号／名称查询 | 身份主要来自 RTK 绑定 |
| 预警类别列 | RTK 匹配列 | 展示当前真实字段 |
| 正常／报警／待复核结果 | 有效／无效结果 | 与 C++ volume_valid 对齐 |
| 详情／处理 | 详情 | 没有人工处理落库接口 |
| 偏移、超斗廓占位 | RTK、速度，以及下方模板配准信息 | 当前算法和新表可提供的字段不同 |
| 旧照片读取 | 保留照片框，暂无照片 | MySQL 没有照片索引 |
| 正常、报警、待复核统计 Excel | 总车次、有效、无效、有效体积及新明细 | 全部数据口径一致 |

保留原设计稿六个菜单、色彩、顶部与侧栏尺寸、查询顺序、表格与弹窗风格。未接入的四页保留布局并明确提示。未做三维预览、设备配置下发或用户编辑。

## 证据与交付

- [原始设计稿参照](evidence/mysql-20261109/before-reference-home.png)
- [新版首页](evidence/mysql-20261109/home-1366.png)
- [查询与统计](evidence/mysql-20261109/query.png)
- [配准详情](evidence/mysql-20261109/detail-metrics.png)
- [无效测量详情](evidence/mysql-20261109/detail-invalid.png)
- [周报预览](evidence/mysql-20261109/export.png)
- [60 项自动检查报告](evidence/mysql-20261109/api-report.json)
- [浏览器报告](evidence/mysql-20261109/browser-report.json)
- [部署记录](evidence/mysql-20261109/deployment-report.json)
- [详细交接文档](../MYSQL_IMPLEMENTATION.md)

原设计稿截图与数百条记录的新版截图只用于视觉和隔离测试对照；不代表正式测量数据。

正式库只读验证：首页 0 条、查询 0 条，Excel 正常生成（8156 字节）。移除开发工具 PATH 后，win-x64 自包含包仍能运行。生产凭据未写入代码、公开配置或文档。

备份目录：`.backups/mysql-20261109-215155`，包含 source-before 和 package-before。账号文件、原 Cookie 密钥、appsettings.Local.json 的哈希比对一致。新版 C++ 49 个受检源码／配置／测试文件的哈希没有变化；原 HTML 设计稿哈希没有变化。

当前服务：<http://127.0.0.1:8081>。原桌面快捷方式仍使用原项目启动脚本。旧 Java 服务 8080 未做修改。

