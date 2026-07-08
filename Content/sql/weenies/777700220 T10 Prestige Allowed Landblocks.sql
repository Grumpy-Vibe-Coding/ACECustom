-- Clean up existing Tier 1 entries
DELETE FROM `prestige_allowed_landblocks` WHERE `tier` = 1;

-- Insert exact T10 landblocks for Prestige Tier 1
INSERT INTO `prestige_allowed_landblocks`
    (`tier`, `landblock`, `is_active`, `updated_at`)
VALUES
    (1, 63339, 1, UTC_TIMESTAMP()),  -- 0xF76B
    (1, 63340, 1, UTC_TIMESTAMP()),  -- 0xF76C
    (1, 63595, 1, UTC_TIMESTAMP()),  -- 0xF86B
    (1, 63594, 1, UTC_TIMESTAMP()),  -- 0xF86A
    (1, 63850, 1, UTC_TIMESTAMP()),  -- 0xF96A
    (1, 63849, 1, UTC_TIMESTAMP()),  -- 0xF969
    (1, 63848, 1, UTC_TIMESTAMP()),  -- 0xF968
    (1, 63847, 1, UTC_TIMESTAMP()),  -- 0xF967
    (1, 63846, 1, UTC_TIMESTAMP()),  -- 0xF966
    (1, 63845, 1, UTC_TIMESTAMP()),  -- 0xF965
    (1, 63844, 1, UTC_TIMESTAMP()),  -- 0xF964
    (1, 63843, 1, UTC_TIMESTAMP()),  -- 0xF963
    (1, 63842, 1, UTC_TIMESTAMP()),  -- 0xF962
    (1, 63586, 1, UTC_TIMESTAMP()),  -- 0xF862
    (1, 63330, 1, UTC_TIMESTAMP()),  -- 0xF762
    (1, 63074, 1, UTC_TIMESTAMP()),  -- 0xF662
    (1, 62818, 1, UTC_TIMESTAMP()),  -- 0xF562
    (1, 62819, 1, UTC_TIMESTAMP()),  -- 0xF563
    (1, 63076, 1, UTC_TIMESTAMP()),  -- 0xF664
    (1, 63077, 1, UTC_TIMESTAMP()),  -- 0xF665
    (1, 63334, 1, UTC_TIMESTAMP()),  -- 0xF766
    (1, 63335, 1, UTC_TIMESTAMP()),  -- 0xF767
    (1, 63079, 1, UTC_TIMESTAMP()),  -- 0xF667
    (1, 62823, 1, UTC_TIMESTAMP()),  -- 0xF567
    (1, 62824, 1, UTC_TIMESTAMP()),  -- 0xF568
    (1, 63081, 1, UTC_TIMESTAMP()),  -- 0xF669
    (1, 63337, 1, UTC_TIMESTAMP()),  -- 0xF769
    (1, 63338, 1, UTC_TIMESTAMP()),  -- 0xF76A
    (1, 63593, 1, UTC_TIMESTAMP()),  -- 0xF869
    (1, 63592, 1, UTC_TIMESTAMP()),  -- 0xF868
    (1, 63336, 1, UTC_TIMESTAMP()),  -- 0xF768
    (1, 63591, 1, UTC_TIMESTAMP()),  -- 0xF867
    (1, 63590, 1, UTC_TIMESTAMP()),  -- 0xF866
    (1, 63589, 1, UTC_TIMESTAMP()),  -- 0xF865
    (1, 63333, 1, UTC_TIMESTAMP()),  -- 0xF765
    (1, 63332, 1, UTC_TIMESTAMP()),  -- 0xF764
    (1, 63588, 1, UTC_TIMESTAMP()),  -- 0xF864
    (1, 63587, 1, UTC_TIMESTAMP()),  -- 0xF863
    (1, 63331, 1, UTC_TIMESTAMP())  -- 0xF763
ON DUPLICATE KEY UPDATE `is_active`=1, `updated_at`=UTC_TIMESTAMP();
