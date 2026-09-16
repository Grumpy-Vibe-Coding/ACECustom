-- Per-placement size: a placed object's own scale (PropertyFloat.DefaultScale), NULL = the weenie's size (unchanged).
-- Set live with /scaleinst <guid> <scale|reset>. Must run BEFORE any server build that has LandblockInstance.Scale,
-- or instance queries fail. Exported landblock SQL only names `scale` on rows that have one.
ALTER TABLE `landblock_instance`
ADD COLUMN `scale` FLOAT NULL DEFAULT NULL AFTER `variation_Id`;
