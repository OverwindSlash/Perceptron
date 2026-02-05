-- events definition

CREATE TABLE `events` (
                          `EventId` varchar(36) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL COMMENT '事件唯一标识符 (GUID)',
                          `Timestamp` datetime NOT NULL COMMENT '事件时间戳',
                          `SourceId` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL COMMENT '事件源标识',
                          `EventType` varchar(100) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL COMMENT '事件类型',
                          `EventName` varchar(100) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL COMMENT '事件名称',
                          `AlgorithmName` varchar(100) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL COMMENT '算法名称',
                          `Message` text CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci COMMENT '事件消息',
                          `BucketName` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci DEFAULT NULL COMMENT '存储桶名称',
                          `ImageId` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci DEFAULT NULL COMMENT '图像标识',
                          `VideoId` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci DEFAULT NULL COMMENT '视频标识',
                          PRIMARY KEY (`EventId`),
                          KEY `idx_timestamp` (`Timestamp`),
                          KEY `idx_source_id` (`SourceId`),
                          KEY `idx_event_type` (`EventType`),
                          KEY `idx_event_name` (`EventName`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='领域事件存储表';