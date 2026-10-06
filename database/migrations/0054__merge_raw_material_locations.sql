/*
================================================================================
KAZUKO ERP JEWELRY - DATABASE MIGRATION SCRIPT
================================================================================
Migration Version: 54
Migration Name:    MERGE_RAW_MATERIAL_LOCATIONS
Description:       Consolidate extra RAW_MATERIAL locations (e.g. KHO_THU_HOI)
                   into the primary NVL warehouse per branch; deactivate extras.
Target Database:   Firebird 5.0 (Dialect 3, UTF8)
================================================================================
*/

SET AUTODDL OFF;

CREATE EXCEPTION EX_MIGRATION_CHECK 'Migration 54 validation failed';

SET TERM ^ ;
EXECUTE BLOCK AS
DECLARE VARIABLE v_cnt INTEGER;
BEGIN
    SELECT COUNT(*) FROM SCHEMA_MIGRATIONS WHERE VERSION_NO = 54 INTO :v_cnt;
    IF (:v_cnt > 0) THEN
        EXCEPTION EX_MIGRATION_CHECK 'Phien ban migration 54 da ton tai.';

    SELECT COUNT(*) FROM SCHEMA_MIGRATIONS WHERE VERSION_NO = 53 INTO :v_cnt;
    IF (:v_cnt <> 1) THEN
        EXCEPTION EX_MIGRATION_CHECK 'Can migration 53 truoc migration 54.';
END^
SET TERM ; ^

COMMIT;

SET TERM ^ ;
EXECUTE BLOCK AS
DECLARE VARIABLE v_branch_id BIGINT;
DECLARE VARIABLE v_target BIGINT;
DECLARE VARIABLE v_legacy BIGINT;
DECLARE VARIABLE v_mat_id BIGINT;
DECLARE VARIABLE v_qty NUMERIC(18,4);
DECLARE VARIABLE v_cost NUMERIC(18,4);
DECLARE VARIABLE v_exist INTEGER;
BEGIN
    FOR SELECT DISTINCT BRANCH_ID
          FROM LOCATIONS
         WHERE INVENTORY_SCOPE = 'RAW_MATERIAL'
           AND IS_ACTIVE = TRUE
        INTO :v_branch_id
    DO
    BEGIN
        /* Prefer KHO_TRUNG_TAM, then KHO_NVL_*, then lowest ID */
        SELECT FIRST 1 ID
          FROM LOCATIONS
         WHERE BRANCH_ID = :v_branch_id
           AND INVENTORY_SCOPE = 'RAW_MATERIAL'
           AND IS_ACTIVE = TRUE
         ORDER BY CASE
                    WHEN CODE = 'KHO_TRUNG_TAM' THEN 0
                    WHEN CODE STARTING WITH 'KHO_NVL' THEN 1
                    ELSE 2
                  END,
                  ID
          INTO :v_target;

        IF (:v_target IS NULL) THEN
            EXCEPTION EX_MIGRATION_CHECK 'Thieu kho RAW_MATERIAL active.';

        FOR SELECT ID
              FROM LOCATIONS
             WHERE BRANCH_ID = :v_branch_id
               AND INVENTORY_SCOPE = 'RAW_MATERIAL'
               AND IS_ACTIVE = TRUE
               AND ID <> :v_target
            INTO :v_legacy
        DO
        BEGIN
            /* Merge stock balances into target */
            FOR SELECT MATERIAL_ID, QUANTITY, AVERAGE_COST
                  FROM STOCK_BALANCES
                 WHERE LOCATION_ID = :v_legacy
                INTO :v_mat_id, :v_qty, :v_cost
            DO
            BEGIN
                SELECT COUNT(*) FROM STOCK_BALANCES
                 WHERE MATERIAL_ID = :v_mat_id AND LOCATION_ID = :v_target
                INTO :v_exist;

                IF (:v_exist > 0) THEN
                    UPDATE STOCK_BALANCES
                       SET QUANTITY = QUANTITY + :v_qty,
                           AVERAGE_COST = CASE
                               WHEN (QUANTITY + :v_qty) = 0 THEN 0
                               ELSE ((QUANTITY * AVERAGE_COST) + (:v_qty * :v_cost))
                                    / (QUANTITY + :v_qty)
                           END,
                           UPDATED_AT = CURRENT_TIMESTAMP
                     WHERE MATERIAL_ID = :v_mat_id AND LOCATION_ID = :v_target;
                ELSE
                    UPDATE STOCK_BALANCES
                       SET LOCATION_ID = :v_target,
                           UPDATED_AT = CURRENT_TIMESTAMP
                     WHERE MATERIAL_ID = :v_mat_id AND LOCATION_ID = :v_legacy;
            END

            DELETE FROM STOCK_BALANCES WHERE LOCATION_ID = :v_legacy;

            UPDATE STOCK_MOVEMENTS
               SET LOCATION_ID = :v_target
             WHERE LOCATION_ID = :v_legacy;

            UPDATE STOCK_DOCUMENTS
               SET SOURCE_LOCATION_ID = :v_target
             WHERE SOURCE_LOCATION_ID = :v_legacy;

            UPDATE STOCK_DOCUMENTS
               SET DESTINATION_LOCATION_ID = :v_target
             WHERE DESTINATION_LOCATION_ID = :v_legacy;

            UPDATE FINISHED_PRODUCTS
               SET LOCATION_ID = :v_target,
                   ROW_VERSION = ROW_VERSION + 1
             WHERE LOCATION_ID = :v_legacy;

            UPDATE FINISHED_PRODUCT_MOVEMENTS
               SET FROM_LOCATION_ID = :v_target
             WHERE FROM_LOCATION_ID = :v_legacy;

            UPDATE FINISHED_PRODUCT_MOVEMENTS
               SET TO_LOCATION_ID = :v_target
             WHERE TO_LOCATION_ID = :v_legacy;

            UPDATE FINISHED_PRODUCT_MOVEMENTS
               SET LOCATION_ID = :v_target
             WHERE LOCATION_ID = :v_legacy;

            UPDATE FINISHED_PRODUCT_DOCUMENT_LINES
               SET SOURCE_LOCATION_ID = :v_target
             WHERE SOURCE_LOCATION_ID = :v_legacy;

            UPDATE FINISHED_PRODUCT_DOCUMENT_LINES
               SET DESTINATION_LOCATION_ID = :v_target
             WHERE DESTINATION_LOCATION_ID = :v_legacy;

            UPDATE PRODUCTION_ORDERS
               SET SOURCE_LOCATION_ID = :v_target
             WHERE SOURCE_LOCATION_ID = :v_legacy;

            UPDATE SALES_CHECKOUTS
               SET OLD_GOLD_DESTINATION_LOCATION_ID = :v_target
             WHERE OLD_GOLD_DESTINATION_LOCATION_ID = :v_legacy;

            UPDATE SALES_CHECKOUTS
               SET SOURCE_LOCATION_ID = :v_target
             WHERE SOURCE_LOCATION_ID = :v_legacy;

            UPDATE STOCKTAKES
               SET LOCATION_ID = :v_target
             WHERE LOCATION_ID = :v_legacy;

            UPDATE STOCK_CRAFT_OUTPUT_LINES
               SET DESTINATION_LOCATION_ID = :v_target
             WHERE DESTINATION_LOCATION_ID = :v_legacy;

            UPDATE LOCATIONS
               SET IS_ACTIVE = FALSE,
                   NAME = NAME || ' (đã gộp)'
             WHERE ID = :v_legacy;
        END

        /* Canonical display name on surviving NVL warehouse */
        UPDATE LOCATIONS
           SET NAME = 'Kho nguyên vật liệu'
         WHERE ID = :v_target;
    END

    /* Exactly one active RAW_MATERIAL per active branch */
    IF (EXISTS (
        SELECT 1 FROM BRANCHES B
        WHERE B.IS_ACTIVE = TRUE
          AND (
              SELECT COUNT(*) FROM LOCATIONS L
               WHERE L.BRANCH_ID = B.ID
                 AND L.IS_ACTIVE = TRUE
                 AND L.INVENTORY_SCOPE = 'RAW_MATERIAL'
          ) <> 1
    )) THEN
        EXCEPTION EX_MIGRATION_CHECK 'Moi chi nhanh active phai co dung 1 kho NVL.';
END^
SET TERM ; ^

COMMIT;

DROP EXCEPTION EX_MIGRATION_CHECK;

COMMIT;
