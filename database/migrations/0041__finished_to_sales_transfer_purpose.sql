/*
================================================================================
KAZUKO ERP JEWELRY - DATABASE MIGRATION SCRIPT
================================================================================
Migration Version: 41
Migration Name:    FINISHED_TO_SALES_TRANSFER_PURPOSE
Description:       Allow stock transfer documents that move unique finished
                   products from finished-goods storage to the sales floor.
Target Database:   Firebird 5.0 (Dialect 3, UTF8)
================================================================================
*/

SET AUTODDL OFF;

CREATE EXCEPTION EX_MIGRATION_CHECK 'Migration validation error';

SET TERM ^ ;
EXECUTE BLOCK AS
DECLARE VARIABLE v_cnt INTEGER;
BEGIN
    SELECT COUNT(*) FROM SCHEMA_MIGRATIONS WHERE VERSION_NO = 41 INTO :v_cnt;
    IF (:v_cnt > 0) THEN
        EXCEPTION EX_MIGRATION_CHECK 'Phien ban migration 41 da ton tai.';

    SELECT COUNT(*) FROM SCHEMA_MIGRATIONS WHERE VERSION_NO = 40 INTO :v_cnt;
    IF (:v_cnt = 0) THEN
        EXCEPTION EX_MIGRATION_CHECK 'Can migration 40 truoc migration 41.';

    SELECT COUNT(*)
    FROM RDB$RELATION_CONSTRAINTS
    WHERE RDB$RELATION_NAME = 'STOCK_DOCUMENTS'
      AND RDB$CONSTRAINT_NAME = 'CK_STOCK_PURPOSE'
    INTO :v_cnt;
    IF (:v_cnt = 0) THEN
        EXCEPTION EX_MIGRATION_CHECK 'Khong tim thay CK_STOCK_PURPOSE.';
END^
SET TERM ; ^

ALTER TABLE STOCK_DOCUMENTS DROP CONSTRAINT CK_STOCK_PURPOSE;
ALTER TABLE STOCK_DOCUMENTS ADD CONSTRAINT CK_STOCK_PURPOSE
    CHECK (PURPOSE_CODE IS NULL OR PURPOSE_CODE IN (
        'GENERAL', 'PRODUCTION_ISSUE', 'SUPPLEMENT_ISSUE', 'MATERIAL_RETURN',
        'SCRAP_RECOVERY', 'FINISHED_RECEIPT', 'OLD_GOLD_PURCHASE',
        'SUPPLIER_PURCHASE', 'OUTSOURCED_CRAFT', 'CRAFT_SETTLEMENT',
        'FINISHED_GOODS_DELIVERY', 'FINISHED_TO_SALES', 'STOCK_ADJUST'));

DROP EXCEPTION EX_MIGRATION_CHECK;
