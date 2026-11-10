-- vehicle_id 与 RTK 上报编号一致，使用二进制排序规则精确区分大小写。
CREATE TABLE IF NOT EXISTS vehicles (
    vehicle_id VARCHAR(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin PRIMARY KEY,
    name VARCHAR(128) NOT NULL DEFAULT '', -- 矿内自定义车辆别名，沿用既有字段以保留数据。
    company VARCHAR(128) NOT NULL DEFAULT '',
    model_id VARCHAR(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL DEFAULT '',
    model_name VARCHAR(128) NOT NULL DEFAULT '',
    updated_at_ms BIGINT NOT NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- 每辆车仅存最新定位，时间戳防止重复或乱序数据覆盖。
CREATE TABLE IF NOT EXISTS gps_positions (
    vehicle_id VARCHAR(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin PRIMARY KEY,
    position_time_ms BIGINT NOT NULL,
    latitude DOUBLE NOT NULL,
    longitude DOUBLE NOT NULL,
    speed_mps DOUBLE NOT NULL,
    heading_deg DOUBLE NOT NULL,
    accuracy_m DOUBLE NOT NULL,
    fix_quality VARCHAR(16) NOT NULL,
    received_at_ms BIGINT NOT NULL,
    INDEX idx_gps_time(position_time_ms),
    CONSTRAINT fk_gps_vehicle FOREIGN KEY(vehicle_id) REFERENCES vehicles(vehicle_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- event_id 保证补存幂等；保留车辆名称快照，不以外键关联可删除的车辆档案。
-- 无效体积保存为 NULL，volume_valid 用于区分有效的零体积。
CREATE TABLE IF NOT EXISTS measurements (
    event_id VARCHAR(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin PRIMARY KEY,
    vehicle_id VARCHAR(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NULL,
    vehicle_name VARCHAR(128) NOT NULL DEFAULT '',
    started_at_ms BIGINT NOT NULL,
    ended_at_ms BIGINT NOT NULL,
    match_status VARCHAR(32) NOT NULL,
    volume_valid BOOLEAN NOT NULL,
    volume_m3 DOUBLE NULL,
    speed_mps DOUBLE NULL,
    reason TEXT NOT NULL,
    calibration_id VARCHAR(256) NOT NULL DEFAULT '',
    latitude DOUBLE NULL,
    longitude DOUBLE NULL,
    accuracy_m DOUBLE NULL,
    gps_time_ms BIGINT NULL,
    scan_file TEXT NOT NULL,
    result_file TEXT NOT NULL,
    source VARCHAR(32) NOT NULL DEFAULT 'RTK_LIDAR',
    legacy_metadata JSON NULL,
    template_id VARCHAR(64) NULL,
    template_name VARCHAR(128) NULL,
    registration_rmse_m DOUBLE NULL,
    registration_fitness DOUBLE NULL,
    cloud_coverage DOUBLE NULL,
    registration_transform JSON NULL,
    algorithm_version VARCHAR(64) NULL,
    cloud_file TEXT NULL,
    trajectory_file TEXT NULL,
    motion_metadata JSON NULL,
    INDEX idx_measurement_time(started_at_ms,event_id),
    INDEX idx_measurement_vehicle_time(vehicle_id,started_at_ms),
    INDEX idx_measurement_valid_time(volume_valid,started_at_ms)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
