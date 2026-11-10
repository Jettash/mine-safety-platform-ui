# 矿车网页端 MySQL 改造与交接说明

实施日期：2026-11-09  
网页版本：2.0.0  
网页项目：`E:\BaiduNetdiskDownload\mine-safety-web`  
对照的新版 C++：`E:\code\mine-safety-platform\mine-safety-platform`

## 1. 这次完成了什么

网页继续使用原生 HTML、CSS、JavaScript，后端继续使用 C# ASP.NET Core。没有引入 Vue 或 Spring Boot。

这次把旧版读取 SQLite 的网页升级为读取新版 C++ 的 MySQL，并同步调整首页、查询、详情和 Excel 的数据含义。原设计稿的顶部、侧栏、颜色、六个页面、查询操作顺序和弹窗风格继续保留。

C++ 仍然负责雷达、RTK、标定模板、配准与载量计算。网页负责查询已经保存的结果，不参与重新计算，不修改测量结果，也不迁移正式数据库。

## 2. 整个链路

```text
顶部雷达 + 侧面雷达 + RTK
             │
             ▼
C++ / Qt：车辆匹配、运动重建、模板配准、载量计算
             │
             ├─ MySQL mine_truck：车辆、最新定位、测量结果
             └─ C++ data/events：扫描、result.json、点云、轨迹等归档
                         │
                         ▼
独立网页服务 MineSafety.Web.exe
   C# + MySqlConnector，用 mine_reader 账号只读查询
                         │
                         ▼
矿区局域网浏览器：HTML + CSS + JavaScript
```

网页与 C++ 是两个独立程序。Qt 停止后，只要 MySQL 和网页服务仍在运行，历史记录仍可查询。MySQL 停止时，网页会明确报错，不能继续读取新的数据库结果。

网页不会控制雷达，也不会把数据库中的结果当成当前雷达在线状态。

## 3. 新旧区别

| 内容 | 旧网页 | 本次版本 |
|---|---|---|
| 数据库 | SQLite 的 edge.db | MySQL 的 mine_truck |
| 核心记录表 | passage_event | measurements |
| 车辆条件 | 车牌及旧车牌规范化 | 车辆编号、历史名称、当前档案名称 |
| 状态 | 正常／报警／待复核 | 有效／无效测量，RTK 匹配独立显示 |
| 首页四卡片 | 今日过车、正常、报警、待复核 | 今日过车、有效测量、无效测量、有效体积 |
| 详情 | 旧版照片与偏移、超斗廓占位 | RTK、速度、模板、配准指标、算法版本、原因、GPS |
| 照片 | 查询旧照片索引 | 新库没有照片索引，保留框并显示暂无照片 |
| Excel | 车牌与旧检测状态 | 车辆编号、历史名称、有效性、RTK、模板与指标等 |
| 运行端口 | 原默认 8080，本机覆盖 8081 | 默认直接设为 8081 |
| 查询一致性 | 冻结查询快照 | 继续保留，分页、详情、预览、导出一致 |

新 C++ 的第二台雷达参与侧面运动估计和点云重建。网页里的“辅助雷达”沿用原页面命名，不能理解为它只做独立测速。

## 4. 页面怎么用

### 首页

打开并登录后，显示北京时间当日的所有 RTK_LIDAR 测量记录：

- 今日过车：当天已经入库的测量记录数量。
- 有效测量：测量端标记有效，且体积为非负有效数值的数量。
- 无效测量：其余结果。
- 有效体积：只把有效记录的体积相加，单位 m³。

首页每 5 秒更新，最近记录只展示最新 5 条；统计覆盖当天全部匹配记录，不只统计这 5 条，也不受 C++ 桌面记录列表 500 条限制。

页面进入后台或切到其他菜单时暂停定时刷新；返回首页会刷新。页面显示更新时间。数据库故障时保留上次显示值并明确显示“更新失败”，不能把旧值当成刚刚读取成功的结果。

这属于定时刷新已入库记录，不是实时点云、采集中状态或车辆尚未计算完成的进度。

### 查询与报表

1. 选择今日、本周、本月或自定义时间。
2. 可选填写车辆编号或名称。
3. 点击“查询”。
4. 查看完整范围统计，按 10／20／50 条翻页。
5. 点“详情”查看对应记录。
6. 点击“导出报表”，按日、周、月预览并下载 Excel。

修改筛选条件后，原结果仍可查看，但导出按钮会禁用，提示重新查询。新车辆入库不会挤动已经查出的分页，也不会改变本次详情、预览和 Excel。

时间是记录的 `started_at_ms`，详情同时给出结束时间。开始时间包含，结束时间包含所选分钟的最后一毫秒。本周从北京时间周一开始。

### 详情

主要字段：

- 车辆编号、记录时的名称、事件编号、开始与结束时间。
- 有效／无效、体积、RTK 匹配状态、速度。
- 空车模板编号与名称。
- 配准 RMSE、配准匹配率、点云覆盖率。
- 算法版本、标定编号、原始原因。
- 测量记录保存的经纬度、RTK 精度和 RTK 时间。

原因保留 C++ 的原始文本／代码，方便与你们的测量端和论文实验记录对照，网页不猜测新的故障结论。

照片保持原来两个位置；没有真实数据索引时显示“暂无照片”。三维点云浏览本次未接入。

### 其余四页

设备状态、配置管理、用户管理、邮箱管理继续保留原布局：

- 设备状态显示“未接入”，不伪造在线数量。
- 配置输入、通知开关不可实际生效，操作提示未接入。
- 用户列表展示现有 admin、viewer 两个固定账号；不支持新增、删除或在线改密。
- 网页没有写入车辆档案、修改 RTK、人工确认体积或下发雷达配置的接口。

## 5. 数据库与字段对应

正式连接：`127.0.0.1:3306 / mine_truck`。使用现有 `mine_reader@127.0.0.1`，权限为 `SELECT ON mine_truck.*`。

| MySQL 表 | 网页用途 |
|---|---|
| measurements | 首页、历史、详情、统计、导出的唯一测量结果来源 |
| vehicles | 查询时补充匹配当前车辆名称，不覆盖历史名称 |
| gps_positions | 本版不用于历史详情；这是最新定位，不能代替测量时的定位 |

| measurements 字段 | 页面／接口 |
|---|---|
| event_id | 事件编号、稳定排序、快照内查详情 |
| started_at_ms / ended_at_ms | 北京时间的开始／结束时间 |
| vehicle_id / vehicle_name | 编号、记录时名称 |
| match_status | RTK 匹配结果，与体积有效性分开 |
| volume_valid / volume_m3 | 有效性、有效体积 |
| reason | 原始原因，详情与 Excel 保留 |
| speed_mps | 速度，单位 m/s |
| template_id / template_name | 空车模板 |
| registration_rmse_m | 配准 RMSE，单位 m |
| registration_fitness / cloud_coverage | 页面百分数、Excel 百分数格式 |
| algorithm_version / calibration_id | 算法与标定标识 |
| latitude / longitude / accuracy_m / gps_time_ms | 测量时 RTK 快照 |
| source | 仅纳入 RTK_LIDAR |
| scan_file / result_file / cloud_file / trajectory_file 等 | 本版不对浏览器提供归档文件下载或三维预览 |

### 必须保持的口径

1. 默认排除 TEST、DEMO、历史导入等其他来源。网页不会写入演示数据。
2. 有效的 0 m³ 是有效测量；不能用“体积大于零”作为有效性条件。
3. 无效结果即使带有旧数值，也不展示为有效体积、不计入总量。
4. RTK 的 MATCHED 不等于体积有效，模板缺失等情况仍可能无效。
5. 配准指标用于展示；网页不自行设 RMSE 或覆盖率阈值重新判定结果。
6. C++ 用空值或负 RMSE 表示配准未执行时，相关配准指标显示“—”，不把默认 0 当成已测得 0%。
7. 当前档案改名或删除后，已有测量仍显示 measurements.vehicle_name 的历史快照。
8. 车辆编号区分大小写，保留内部空格和中点，不再执行旧的车牌去空格、去中点、转大写。
9. 百分号、下划线等搜索字符按字面搜索，不当成 SQL 通配符。
10. 页面体积显示两位小数；Excel 保存数值、显示两位小数；空值保持空白。网页不替代算法精度验收。

如果库出现“有效标志为真，但体积为 NULL／负数”的矛盾值，网页按无效展示；无法表示的异常体积或非法时间会明确报错，避免输出伪造数字。正式有效性仍应由 C++ 正确写入。

## 6. 关键代码在哪里

所有路径相对于网页项目根目录。

| 文件 | 负责什么 |
|---|---|
| src/MineSafety.Web/Program.cs | HTTP 启动、认证、接口、错误响应、来源校验 |
| src/MineSafety.Web/Models.cs | 配置、北京时间范围、记录与统计模型、RTK 中文标签 |
| src/MineSafety.Web/RecordRepository.cs | 解密连接配置；参数化 MySQL SELECT；只读事务；全量有界查询；首页聚合与最新 5 条 |
| src/MineSafety.Web/QuerySnapshots.cs | 冻结查询、15 分钟过期、会话隔离、缓存预算与分页 |
| src/MineSafety.Web/Reports.cs | 日／周／月分组、统计和明细 Excel、限制同时导出数量 |
| src/MineSafety.Web/Accounts.cs | 两个固定账号、密码哈希、账号安全标记 |
| src/MineSafety.Web/wwwroot/index.html | 六页布局、表格、详情和导出弹窗 |
| src/MineSafety.Web/wwwroot/app.css | 原设计样式、有效／无效标签、新详情字段布局 |
| src/MineSafety.Web/wwwroot/app.js | 5 秒刷新、查询、分页、详情、快照状态、Excel 下载 |
| src/MineSafety.Web/wwwroot/auth.js | 登录、首次初始化交互 |
| src/MineSafety.Web/appsettings.json | 可公开的默认设置；不含数据库密码 |
| scripts/set-mysql-connection.ps1 | 读取密码或既有 MySQL 客户端配置，在内存拼接并 DPAPI 加密保存 |
| scripts/start-server.ps1 / stop-server.ps1 | 启停自己创建的网页进程，通过路径、PID 和启动时间识别进程 |
| scripts/build.ps1 | 编译、验收、发布自包含包，保留账号与本地覆盖配置 |
| scripts/test.ps1 | 自动测试入口，支持 -Browser |
| scripts/check-package.ps1 | 清除开发工具路径后启动发布包，用隔离网页账号只读验证正式库 |
| tests/MineSafety.Web.Tests/MySqlSandbox.cs | 启动独立 MySQL、独立数据目录、随机本机端口；校验 datadir，避免误测正式库 |
| tests/MineSafety.Web.Tests/Fixture.cs | 在隔离库准备边界、异常、500 条以上、RTK 与模板等测试场景 |
| tests/MineSafety.Web.Tests/Program.cs | 接口、口径、快照、认证、Excel、并发、故障验收 |
| tests/MineSafety.Web.Tests/BrowserQa.cs | 用 Edge 验证真实页面、刷新、六页、弹窗、下载和截图 |
| tests/MineSafety.Web.Tests/fixtures/mysql-schema.sql | 新 C++ schema.sql 的测试用副本，只给隔离库建表 |
| reference/design-v4.html | 用户原始设计稿，保留不改 |

## 7. 接口一览

除初始化信息、登录与本机首次初始化外，数据接口都要求登录。

| 方法与路径 | 用途 |
|---|---|
| GET /api/setup-info | 当前是否已经建立网页账号 |
| POST /api/setup | 服务器本机首次建立 admin／viewer；有一次性令牌校验 |
| POST /api/login | 登录；同一 IP 有频率限制 |
| POST /api/logout | 退出 |
| GET /api/me | 当前账号 |
| GET /api/home | 今日全部统计与最近 5 条 |
| POST /api/queries | 创建冻结查询，参数 start、end、vehicle |
| GET /api/queries/{id}?page=1&pageSize=20 | 冻结结果分页 |
| GET /api/queries/{id}/records/{eventId} | 冻结结果内详情 |
| GET /api/queries/{id}/report?grain=day | 日／周／月预览 |
| GET /api/queries/{id}/export.xlsx?grain=day&includeRecords=true | Excel |

旧的车牌参数与旧照片接口不再作为本版兼容接口。前端已同步更新，外部调用方如有使用需同步调整。

## 8. 参数与凭据

`package/appsettings.json` 是默认值；`package/appsettings.Local.json` 是本机覆盖，重新构建时保留。

```json
{
  "Urls": "http://0.0.0.0:8081",
  "Web": {
    "StateDirectory": "private",
    "MySqlConnectionFile": "mysql.connection.dpapi",
    "SnapshotMinutes": 15,
    "MaxQueryRecords": 100000,
    "MaxCachedRecords": 200000,
    "DatabaseTimeoutSeconds": 5
  }
}
```

- StateDirectory 相对于运行包；保存网页账号、Cookie 密钥、运行日志和加密 MySQL 连接文件。
- MySqlConnectionFile 相对于 StateDirectory，也支持绝对路径；不能放在 wwwroot。
- 单次查询最多 100000 条；总快照缓存最多 200000 条，超限报错，不偷偷截断。
- 快照默认 15 分钟；服务重启后快照丢失，重新查询即可。
- 数据库连接／命令超时默认 5 秒；连接池上限 10。
- Excel 同时只生成一个，繁忙时提示稍后重试。
- 日常运行无需设置明文连接字符串。进程环境变量 Web__MySqlConnectionString 只供受控部署或自动测试；不要把密码写入代码、命令历史或 appsettings。

连接凭据存放在 `package/private/mysql.connection.dpapi`。这是 Windows 当前用户加密的二进制文件，不能直接搬到另一台电脑或另一个 Windows 用户使用。迁移时重新配置。

重新配置只读账号，在项目目录打开 PowerShell：

```powershell
./configure-mysql.cmd
```

脚本会安全询问 MySQL 密码。也可以在有读取权限的终端复用本机现有只读客户端文件，不把密码放到命令参数中：

```powershell
./configure-mysql.cmd -DefaultsFile 'E:\MineTruckMySQL\reader.cnf'
```

不要使用 C++ 的写入账号或 root 替代 mine_reader。DPAPI 配置必须由未来运行网页服务的同一 Windows 用户生成。

网页账号密码和 MySQL 密码是两套东西：前者是浏览器登录，后者是服务读库。原来的 admin／viewer 账号与密码保留。

## 9. 怎么打开、停止、改代码后更新

在 CMD：

```bat
cd /d E:\BaiduNetdiskDownload\mine-safety-web
start-web.cmd
```

本机地址：<http://127.0.0.1:8081>。桌面“矿车查询网页”快捷方式继续指向这个启动脚本。

同矿区局域网电脑访问：`http://服务器的局域网IPv4:8081`。需要服务器开机、MySQL 运行、网页服务运行，防火墙允许该端口。原有 8080 的 Java 服务不属于本项目，不需要关闭。

停止：

```bat
stop-web.cmd
```

修改源码后：

```bat
build-web.cmd
start-web.cmd
```

源码变化不会让已经发布的 exe 或 package/wwwroot 自动更新。需要重新构建。构建脚本先编译和测试成功再替换发布文件；现有账号和 appsettings.Local.json 保留。正在运行时会重启属于本项目的服务。

浏览器若仍显示旧的静态内容，按 Ctrl+F5 刷新。

普通测试：

```bat
test-web.cmd
```

连同浏览器：

```powershell
./test-web.cmd -Browser
```

默认在本机寻找 `E:\MineTruckMySQL\mysql-8.4.13\bin\mysqld.exe`。其他开发电脑先设置：

```powershell
$env:MINE_TEST_MYSQL_BIN = 'D:\MySQL\bin'
./test-web.cmd -Browser
```

测试实例仅绑定 127.0.0.1、随机端口、独立 datadir。首次初始化需要一些时间。它的无密码 root 仅存在于这个临时隔离实例，用于测试建库；正式实例不受影响。测试结束关闭自己启动的实例并删除测试库和测试账号，datadir 留在 .verification 中可供后续复用。

运行包是 win-x64 自包含版本。甲方机器运行发布包不需要开发 SDK；开发／构建仍需 .NET SDK 10.0.403。

## 10. 验收与证据

本次使用与新版 C++ 相同的表结构建立隔离 MySQL 8.4 实例验收，没有把测试记录写入正式 mine_truck。

已完成的独立测试包括：

- 528 条当天记录，全部统计与导出，超过桌面 500 条范围。
- 有效零、无效空体积、匹配成功但体积无效、配准未运行的默认零、低配准指标仍遵从 C++ 有效性。
- 排除 TEST／DEMO／历史来源。
- 编号大小写、名称改动、目录删除后的历史快照、特殊字符、SQL 参数化。
- 结束分钟、跨日、跨周、跨月、空结果。
- 翻页、详情、Excel 一致，查询后新增／修改不影响原快照。
- TTL、缓存预算、单次条数上限、只读账号拒绝写入。
- C++ 类似持续写入场景下的并发读取。
- 表暂时不可用时返回明确的 503；测试日志中的 MySQL 1146 是这一故障用例主动触发。
- 登录保护、账号密码哈希、不同会话快照隔离、跨来源 POST 拒绝、退出。
- Excel 的零为数值、无效体积为空白、用户文本不变成公式。

浏览器验收覆盖六页、1366／1920 宽度、查询分页、详情、缺失照片、导出预览与实际 Excel 下载、5 秒刷新、admin／viewer、无未捕获 JavaScript 异常。

顶部、侧栏、主内容和首页卡片几何尺寸与原设计稿对照一致；最新 5 条表格在本次截图中尺寸也一致。截图里的数百条记录仅为隔离验收样本，不能当成现场实测数据。

证据目录：

- `docs/evidence/mysql-20261109/`：界面截图、自动验收报告。
- `.verification/package/report.json`：自包含发布包读取正式库的验证结果。
- `docs/MYSQL_UPGRADE_LOG.md`：改造记录与界面前后变化。

正式库目前没有测量记录，因此交付时看到零车次与暂无记录是正常现象。正式库读库、运行包与启动检查的最终结果见验收文档。

## 11. 常见问题与排查

| 现象 | 先检查 |
|---|---|
| 网页打不开 | 是否执行 start-web.cmd，8081 是否被占用，package 是否完整 |
| 首页提示连接配置缺失／解密失败 | 是否生成 DPAPI 文件，运行用户是否与生成用户一致 |
| MySQL 暂时无法读取 | MySQL 服务、端口、账号密码、SELECT 权限和新版表结构 |
| 首页有车，历史查询不变 | 历史使用冻结结果；点击查询更新 |
| 看到 0.00 m³ | 如果是“有效”，说明测量端写入有效零；不是空值 |
| 测量“无效”但 RTK“已匹配” | 身份匹配成功，载量算法仍可能失败；查看原始原因 |
| 配准指标为“—” | 字段缺失，或者本次配准没有执行；不能解释成精度为零 |
| 设备状态一直未接入 | 本版没有可靠的数据库设备心跳，尚未做设备接入 |
| 查询过期／服务重启后原分页不能用 | 重新点击查询 |
| 改代码没变化 | build-web.cmd 重新发布，然后 Ctrl+F5 |
| 其他电脑打不开，本机能开 | 局域网 IP、交换机／网段、防火墙、监听地址 |

运行日志：`package/private/runtime/server.log`、`server-error.log`。不要把 private 目录发到公开仓库或给前端直接访问。

## 12. 交接与下一步

这次交付的是“测量结果入库后，甲方能在局域网查询与导出”的完整首版。

以下功能仍需要 C++ 或现场条件支持，网页没有伪造完成：

- 真车载量精度、重复性和论文实验验证。
- 采集中状态与设备心跳落库／接口。
- 实际照片采集与稳定照片索引。
- 三维点云与轨迹预览。
- 车辆档案维护、配置下发、在线账号管理、邮件、人工复核。
- 自动随系统启动、HTTPS／更完整的账号体系可按现场运维要求另行增加；当前由启动脚本启动，适用于已约定的矿区局域网。

升级没有更改新版 C++ 的算法、存储代码、表结构或测量数据。需要新增字段时，先与 C++ 维护人员确认字段含义和写入时机，再调整 Models、RecordRepository、前端与 Excel，不能只改页面标签。

本次升级前的源码和原运行包会保存在网页项目的 `.backups` 中。回退应停止网页服务后恢复原运行包；旧版包读的是原 SQLite，不能用它查询新版 MySQL。不要为回退而改动 MySQL 数据。


## 13. 依赖资料

数据库驱动版本锁定为 [MySqlConnector 2.6.2（官方 NuGet）](https://www.nuget.org/packages/MySqlConnector/2.6.2)；代码未使用 ORM。依赖清单与校验值见各项目 packages.lock.json。

## 14. 本次最终交付状态

- 2026-11-09 21:52，实际部署并启动 2.0.0，地址 http://127.0.0.1:8081。
- 最终完成 60 项自动检查；六页浏览器验收、两种宽度、5 秒刷新、Excel 下载通过。
- 实际运行包在移除开发工具 PATH 后启动，使用隔离网页账号完成正式 MySQL 只读查询与导出；正式库当前 0 条，没有加入测试记录。
- 现有 admin／viewer 账号、Cookie 密钥、本机覆盖配置的文件哈希一致。
- C++ 49 个受检文件没有变化；原始 HTML 设计稿 SHA256 为 2B8A3E082EDB4E8F90313E38B8528B9538BC03A9E4238C1F60095DEF1EA2B6FE，未改动。
- 升级前备份：E:\BaiduNetdiskDownload\mine-safety-web\.backups\mysql-20261109-215155。
- 运行整个项目使用 start-web.cmd；只复制 package 目录时使用其中 start.cmd、stop.cmd、configure.cmd。Windows 可能限制直接运行 ps1，本文的 cmd 入口均已包装好，不需要修改系统执行策略。

测试截图和报告见 docs/evidence/mysql-20261109；实际包只读验证见 .verification/package/report.json。临时测试实例已停止；不会让测试 MySQL 与正式实例长期并行运行。
