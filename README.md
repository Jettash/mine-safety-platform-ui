# 矿车载量查询网页端

本项目独立于 C++／Qt 运行，用原生 HTML、CSS、JavaScript 展示已经入库的结果。后台使用 C# ASP.NET Core，以现有只读账号查询新版 C++ 的 MySQL `mine_truck`。

**详细交接文档：[MYSQL_IMPLEMENTATION.md](MYSQL_IMPLEMENTATION.md)**  
改造与界面变化：[docs/MYSQL_UPGRADE_LOG.md](docs/MYSQL_UPGRADE_LOG.md)  
验收结果：[docs/VERIFICATION.md](docs/VERIFICATION.md)

## 立即打开

在 CMD：

```bat
cd /d E:\BaiduNetdiskDownload\mine-safety-web
start-web.cmd
```

本机地址：<http://127.0.0.1:8081>。桌面“矿车查询网页”快捷方式继续可用。

登录使用原来的 admin／viewer 账号密码。正式库暂无测量时，首页显示零与暂无记录。

## 日常命令

| 命令 | 作用 |
|---|---|
| start-web.cmd | 启动网页并打开浏览器，已启动则复用 |
| stop-web.cmd | 停止本项目网页服务 |
| build-web.cmd | 编译、测试、重新发布到 package |
| test-web.cmd | 使用隔离 MySQL 自动验收 |
| test-web.cmd -Browser | 增加 Edge 六页与交互验收 |
| configure-mysql.cmd | 安全输入 MySQL 密码并加密保存连接 |

修改源码以后需要 build-web.cmd，发布的 exe 与网页文件不会自己更新。

首次换机配置可复用已有只读客户端配置：

```bat
configure-mysql.cmd -DefaultsFile E:\MineTruckMySQL\reader.cnf
```

连接文件使用 Windows 当前用户 DPAPI 加密，不能直接换用户／换机复制使用。保留 `package/private` 和 `package/appsettings.Local.json`；不要上传 private 到仓库。

## 当前范围

- 首页每 5 秒刷新今日总车次、有效／无效、有效体积与最新 5 条。
- 车辆编号、历史／当前名称和时间查询；分页、详情、Excel。
- RTK、模板、配准指标和原始原因展示。
- 查询快照默认 15 分钟；单次 10 万条，总缓存 20 万条，超限明确提示。
- 只读取 RTK_LIDAR 记录；有效零体积保留，无效体积不累加。
- 设备、配置、用户编辑、邮箱、照片索引、三维预览尚未接入。

C++ 对照目录为 `E:\code\mine-safety-platform\mine-safety-platform`。本项目不修改 C++ 算法、正式库表结构或记录。

## 开发与部署

目标平台 win-x64，.NET SDK 10.0.403；发布包自带运行时。NuGet 使用 MySqlConnector 2.6.2、ClosedXML 0.105.1 等，锁定文件随代码保存。

自动验收需要本机 MySQL 8.4 的 mysqld.exe；默认路径 `E:\MineTruckMySQL\mysql-8.4.13\bin`，可通过 `MINE_TEST_MYSQL_BIN` 调整。测试会启动随机端口、独立数据目录的实例，不向正式库插入测试记录。

网页默认监听 `0.0.0.0:8081`；同矿区局域网通过服务器 IPv4 访问。旧 Java 服务的 8080 不受影响。

