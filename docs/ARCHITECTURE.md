# 网页端架构（MySQL 2.0）

C++ 负责采集和计算，独立网页负责已经入库结果的查询与导出。完整说明见 [MYSQL_IMPLEMENTATION.md](../MYSQL_IMPLEMENTATION.md)。

## 读取流程

1. 浏览器登录，服务用 HttpOnly／SameSite Cookie 识别账号和会话。
2. 首页在一个短的 REPEATABLE READ、READ ONLY 事务内执行当日聚合与最新 5 条 SELECT。
3. 手动查询执行一条有界 SELECT，最多读取上限加一条；超过上限明确拒绝。
4. 完整结果与统计冻结在会话快照中，随即释放数据库事务。
5. 翻页、详情、报表预览和 Excel 都读取该快照。
6. 新写入只影响首页和下一次主动查询，不改变既有报表。

## 存储边界

- measurements 是结果来源；车辆名称、GPS、模板与配准指标采用记录快照。
- vehicles 仅为搜索当前车辆名称提供 EXISTS 条件；不覆盖历史名称。
- gps_positions 是最新定位，不拿来代替历史定位。
- mine_reader 只有 SELECT，网页不包含建库／迁移／恢复操作。
- 正式凭据使用 Windows CurrentUser DPAPI 保存；账号哈希与 Cookie 密钥均在私有目录。
- 不读取 C++ settings.json 中的写入账号或其他外部服务密码。
- 新库没有照片索引，首版不开放任意文件／归档文件下载。

## 数据与资源约束

北京时间、开始包含、结束分钟包含、周一为每周开始；排序 started_at_ms DESC,event_id DESC。车辆编号保留大小写、空格、中点，查询参数化并转义 LIKE 通配符。

仅 source=RTK_LIDAR；有效零保留。网页不添加算法质量阈值，RTK 匹配不代替体积有效性。无 RMSE 的未执行配准不显示默认百分数。

快照默认 15 分钟、单次 10 万条、全局 20 万条；空结果也占缓存预算。不同登录会话不共享快照。Excel 同时只生成一个。数据库连接池最多 10 个连接，默认超时 5 秒。

## 代码

数据：Models.cs、RecordRepository.cs。  
接口和认证：Program.cs、Accounts.cs。  
快照：QuerySnapshots.cs。  
统计／Excel：Reports.cs。  
页面：wwwroot/index.html、app.css、app.js、auth.js。

全部位于 src/MineSafety.Web。详情字段、接口和运行命令见主交接文档。

