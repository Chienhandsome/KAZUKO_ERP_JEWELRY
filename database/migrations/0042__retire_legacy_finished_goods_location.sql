/*
================================================================================
KAZUKO ERP JEWELRY - DATABASE MIGRATION SCRIPT
================================================================================
Migration Version: 42
Migration Name:    RETIRE_LEGACY_FINISHED_GOODS_LOCATION
Description:       Consolidate the legacy QUAY_TRUNG_TAM finished-goods
                   location into KHO_THANH_PHAM without deleting audit history.
Target Database:   Firebird 5.0 (Dialect 3, UTF8)
================================================================================
*/

SET AUTODDL OFF;

CREATE EXCEPTION EX_MIGRATION_CHECK 'Migration validation error';

SET TERM ^ ;
EXECUTE BLOCK AS
DECLARE VARIABLE v_cnt INTEGER;
DECLARE VARIABLE v_legacy BIGINT;
DECLARE VARIABLE v_target BIGINT;
BEGIN
    SELECT COUNT(*) FROM SCHEMA_MIGRATIONS WHERE VERSION_NO = 42 INTO :v_cnt;
    IF (:v_cnt > 0) THEN
        EXCEPTION EX_MIGRATION_CHECK 'Phien ban migration 42 da ton tai.';

    SELECT COUNT(*) FROM SCHEMA_MIGRATIONS WHERE VERSION_NO = 41 INTO :v_cnt;
    IF (:v_cnt = 0) THEN
        EXCEPTION EX_MIGRATION_CHECK 'Can migration 41 truoc migration 42.';

    SELECT FIRST 1 ID FROM LOCATIONS WHERE CODE = 'QUAY_TRUNG_TAM' INTO :v_legacy;
    SELECT FIRST 1 ID FROM LOCATIONS WHERE CODE = 'KHO_THANH_PHAM' INTO :v_target;
    IF (:v_legacy IS NULL) THEN
        EXCEPTION EX_MIGRATION_CHECK 'Khong tim thay QUAY_TRUNG_TAM.';
    IF (:v_target IS NULL) THEN
        EXCEPTION EX_MIGRATION_CHECK 'Khong tim thay KHO_THANH_PHAM.';

    UPDATE FINISHED_PRODUCTS
       SET LOCATION_ID = :v_target,
           ROW_VERSION = ROW_VERSION + 1
     WHERE LOCATION_ID = :v_legacy;

    UPDATE LOCATIONS
       SET IS_ACTIVE = FALSE,
           INVENTORY_SCOPE = 'GENERAL'
     WHERE ID = :v_legacy;
END^
SET TERM ; ^

DROP EXCEPTION EX_MIGRATION_CHECK;
