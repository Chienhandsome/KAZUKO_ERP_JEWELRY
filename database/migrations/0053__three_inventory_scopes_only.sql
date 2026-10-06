/*
================================================================================
KAZUKO ERP JEWELRY - DATABASE MIGRATION SCRIPT
================================================================================
Migration Version: 53
Migration Name:    THREE_INVENTORY_SCOPES_ONLY
Description:       Restrict LOCATIONS.INVENTORY_SCOPE to RAW_MATERIAL,
                   FINISHED_GOODS, SALES_FLOOR only. Remap legacy GENERAL/SERVICE.
Target Database:   Firebird 5.0 (Dialect 3, UTF8)
================================================================================
*/

SET AUTODDL OFF;

CREATE EXCEPTION EX_MIGRATION_CHECK 'Migration 53 validation failed';

SET TERM ^ ;
EXECUTE BLOCK AS
DECLARE VARIABLE v_cnt INTEGER;
BEGIN
    SELECT COUNT(*) FROM SCHEMA_MIGRATIONS WHERE VERSION_NO = 53 INTO :v_cnt;
    IF (:v_cnt > 0) THEN
        EXCEPTION EX_MIGRATION_CHECK 'Phien ban migration 53 da ton tai.';

    SELECT COUNT(*) FROM SCHEMA_MIGRATIONS WHERE VERSION_NO = 52 INTO :v_cnt;
    IF (:v_cnt <> 1) THEN
        EXCEPTION EX_MIGRATION_CHECK 'Can migration 52 truoc migration 53.';

    SELECT COUNT(*) FROM RDB$RELATIONS
    WHERE RDB$RELATION_NAME = 'LOCATIONS'
    INTO :v_cnt;
    IF (:v_cnt <> 1) THEN
        EXCEPTION EX_MIGRATION_CHECK 'Thieu bang LOCATIONS.';
END^
SET TERM ; ^

COMMIT;

/* Remap legacy scopes before tightening CHECK */
SET TERM ^ ;
EXECUTE BLOCK AS
DECLARE VARIABLE v_cnt INTEGER;
BEGIN
    /* Retired counter that used to be finished goods */
    UPDATE LOCATIONS
       SET INVENTORY_SCOPE = 'FINISHED_GOODS'
     WHERE INVENTORY_SCOPE = 'GENERAL'
       AND CODE = 'QUAY_TRUNG_TAM';

    /* Any remaining GENERAL/SERVICE with stock stay remappable:
       - active GENERAL → RAW_MATERIAL (NVL fallback)
       - inactive GENERAL/SERVICE → RAW_MATERIAL (keep inactive) */
    UPDATE LOCATIONS
       SET INVENTORY_SCOPE = 'RAW_MATERIAL'
     WHERE INVENTORY_SCOPE IN ('GENERAL', 'SERVICE');

    /* Canonical display names for the three warehouse roles */
    UPDATE LOCATIONS
       SET NAME = 'Kho nguyên vật liệu'
     WHERE CODE = 'KHO_TRUNG_TAM'
       AND INVENTORY_SCOPE = 'RAW_MATERIAL';

    UPDATE LOCATIONS
       SET NAME = 'Kho thành phẩm'
     WHERE CODE = 'KHO_THANH_PHAM'
       AND INVENTORY_SCOPE = 'FINISHED_GOODS';

    UPDATE LOCATIONS
       SET NAME = 'Kho bán hàng'
     WHERE CODE = 'KHO_BAN_HANG'
       AND INVENTORY_SCOPE = 'SALES_FLOOR';

    SELECT COUNT(*) FROM LOCATIONS
     WHERE INVENTORY_SCOPE NOT IN ('RAW_MATERIAL', 'FINISHED_GOODS', 'SALES_FLOOR')
    INTO :v_cnt;
    IF (:v_cnt > 0) THEN
        EXCEPTION EX_MIGRATION_CHECK 'Con location scope khong hop le sau remap.';
END^
SET TERM ; ^

COMMIT;

ALTER TABLE LOCATIONS DROP CONSTRAINT CK_LOCATION_INVENTORY_SCOPE;

ALTER TABLE LOCATIONS ADD CONSTRAINT CK_LOCATION_INVENTORY_SCOPE CHECK (
    INVENTORY_SCOPE IN ('RAW_MATERIAL', 'FINISHED_GOODS', 'SALES_FLOOR'));

ALTER TABLE LOCATIONS ALTER COLUMN INVENTORY_SCOPE SET DEFAULT 'RAW_MATERIAL';

COMMIT;

/* Ensure every active branch has the three warehouse roles */
SET TERM ^ ;
EXECUTE BLOCK AS
DECLARE VARIABLE v_branch_id BIGINT;
DECLARE VARIABLE v_branch_code VARCHAR(40);
DECLARE VARIABLE v_cnt INTEGER;
BEGIN
    FOR SELECT ID, CODE FROM BRANCHES WHERE IS_ACTIVE = TRUE INTO :v_branch_id, :v_branch_code DO
    BEGIN
        SELECT COUNT(*) FROM LOCATIONS
         WHERE BRANCH_ID = :v_branch_id AND IS_ACTIVE = TRUE AND INVENTORY_SCOPE = 'RAW_MATERIAL'
        INTO :v_cnt;
        IF (:v_cnt = 0) THEN
            INSERT INTO LOCATIONS (BRANCH_ID, CODE, NAME, LOCATION_TYPE, INVENTORY_SCOPE, IS_ACTIVE)
            VALUES (:v_branch_id, 'KHO_NVL_' || :v_branch_code, 'Kho nguyên vật liệu', 'WAREHOUSE', 'RAW_MATERIAL', TRUE);

        SELECT COUNT(*) FROM LOCATIONS
         WHERE BRANCH_ID = :v_branch_id AND IS_ACTIVE = TRUE AND INVENTORY_SCOPE = 'FINISHED_GOODS'
        INTO :v_cnt;
        IF (:v_cnt = 0) THEN
            INSERT INTO LOCATIONS (BRANCH_ID, CODE, NAME, LOCATION_TYPE, INVENTORY_SCOPE, IS_ACTIVE)
            VALUES (:v_branch_id, 'KHO_TP_' || :v_branch_code, 'Kho thành phẩm', 'WAREHOUSE', 'FINISHED_GOODS', TRUE);

        SELECT COUNT(*) FROM LOCATIONS
         WHERE BRANCH_ID = :v_branch_id AND IS_ACTIVE = TRUE AND INVENTORY_SCOPE = 'SALES_FLOOR'
        INTO :v_cnt;
        IF (:v_cnt = 0) THEN
            INSERT INTO LOCATIONS (BRANCH_ID, CODE, NAME, LOCATION_TYPE, INVENTORY_SCOPE, IS_ACTIVE)
            VALUES (:v_branch_id, 'KHO_BH_' || :v_branch_code, 'Kho bán hàng', 'WAREHOUSE', 'SALES_FLOOR', TRUE);
    END

    IF (EXISTS (
        SELECT 1 FROM BRANCHES B
        WHERE B.IS_ACTIVE = TRUE
          AND NOT EXISTS (
              SELECT 1 FROM LOCATIONS L
              WHERE L.BRANCH_ID = B.ID AND L.IS_ACTIVE = TRUE
                AND L.INVENTORY_SCOPE = 'RAW_MATERIAL')
    )) THEN
        EXCEPTION EX_MIGRATION_CHECK 'Thieu kho RAW_MATERIAL active.';

    IF (EXISTS (
        SELECT 1 FROM BRANCHES B
        WHERE B.IS_ACTIVE = TRUE
          AND NOT EXISTS (
              SELECT 1 FROM LOCATIONS L
              WHERE L.BRANCH_ID = B.ID AND L.IS_ACTIVE = TRUE
                AND L.INVENTORY_SCOPE = 'FINISHED_GOODS')
    )) THEN
        EXCEPTION EX_MIGRATION_CHECK 'Thieu kho FINISHED_GOODS active.';

    IF (EXISTS (
        SELECT 1 FROM BRANCHES B
        WHERE B.IS_ACTIVE = TRUE
          AND NOT EXISTS (
              SELECT 1 FROM LOCATIONS L
              WHERE L.BRANCH_ID = B.ID AND L.IS_ACTIVE = TRUE
                AND L.INVENTORY_SCOPE = 'SALES_FLOOR')
    )) THEN
        EXCEPTION EX_MIGRATION_CHECK 'Thieu kho SALES_FLOOR active.';
END^
SET TERM ; ^

COMMIT;

DROP EXCEPTION EX_MIGRATION_CHECK;

COMMIT;
