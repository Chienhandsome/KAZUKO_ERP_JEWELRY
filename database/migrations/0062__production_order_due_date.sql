/*
================================================================================
KAZUKO ERP JEWELRY - DATABASE MIGRATION SCRIPT
================================================================================
Migration Version: 62
Migration Name:    PRODUCTION_ORDER_DUE_DATE
Description:       Add DUE_DATE (Hạn hoàn thành) to PRODUCTION_ORDERS.
                   "Ngày giao" (material issue date) is derived from
                   STOCK_DOCUMENTS.DOCUMENT_DATE via MATERIAL_ISSUE_DOCUMENT_ID,
                   so no new column is needed for it.
Target Database:   Firebird 5.0 (Dialect 3, UTF8)
================================================================================
*/

SET AUTODDL OFF;

CREATE EXCEPTION EX_MIGRATION_CHECK 'Migration 62 validation failed';

SET TERM ^ ;
EXECUTE BLOCK AS
DECLARE VARIABLE v_cnt INTEGER;
BEGIN
    SELECT COUNT(*) FROM SCHEMA_MIGRATIONS WHERE VERSION_NO = 62 INTO :v_cnt;
    IF (:v_cnt > 0) THEN
        EXCEPTION EX_MIGRATION_CHECK 'Phien ban migration 62 da ton tai.';

    SELECT COUNT(*) FROM SCHEMA_MIGRATIONS WHERE VERSION_NO = 61 INTO :v_cnt;
    IF (:v_cnt <> 1) THEN
        EXCEPTION EX_MIGRATION_CHECK 'Can migration 61 truoc migration 62.';

    SELECT COUNT(*) FROM RDB$RELATIONS WHERE RDB$RELATION_NAME = 'PRODUCTION_ORDERS' INTO :v_cnt;
    IF (:v_cnt <> 1) THEN
        EXCEPTION EX_MIGRATION_CHECK 'Thieu bang PRODUCTION_ORDERS.';

    SELECT COUNT(*) FROM RDB$RELATION_FIELDS
    WHERE RDB$RELATION_NAME = 'PRODUCTION_ORDERS' AND RDB$FIELD_NAME = 'DUE_DATE' INTO :v_cnt;
    IF (:v_cnt > 0) THEN
        EXCEPTION EX_MIGRATION_CHECK 'Cot DUE_DATE da ton tai.';
END^
SET TERM ; ^

EXECUTE STATEMENT 'ALTER TABLE PRODUCTION_ORDERS ADD DUE_DATE TIMESTAMP';

SET TERM ^ ;
EXECUTE BLOCK AS
DECLARE VARIABLE v_cnt INTEGER;
BEGIN
    SELECT COUNT(*) FROM RDB$RELATION_FIELDS
    WHERE RDB$RELATION_NAME = 'PRODUCTION_ORDERS' AND RDB$FIELD_NAME = 'DUE_DATE'
    INTO :v_cnt;
    IF (:v_cnt <> 1) THEN
        EXCEPTION EX_MIGRATION_CHECK 'Khong tao duoc cot DUE_DATE.';
END^
SET TERM ; ^

DROP EXCEPTION EX_MIGRATION_CHECK;
