/*
================================================================================
KAZUKO ERP JEWELRY - DATABASE MIGRATION SCRIPT
================================================================================
Migration Version: 49
Migration Name:    PRODUCTION_SALES_ORDER_ID_NULLABLE
Description:       Ensure PRODUCTION_ORDERS.SALES_ORDER_ID is nullable so STOCK
                   drafts can omit sales order (legacy WinForms may have NOT NULL).
Target Database:   Firebird 5.0 (Dialect 3, UTF8)

Rollback notes (manual, forward-only ledger):
  1. Preferred recovery in production is restore from pre-migration .fbk.
  2. Re-adding NOT NULL is unsafe while STOCK rows with NULL exist.
================================================================================
*/

SET AUTODDL OFF;

CREATE EXCEPTION EX_MIGRATION_CHECK 'Migration validation error';

SET TERM ^ ;
EXECUTE BLOCK AS
DECLARE VARIABLE v_cnt INTEGER;
BEGIN
    SELECT COUNT(*) FROM SCHEMA_MIGRATIONS WHERE VERSION_NO = 49 INTO :v_cnt;
    IF (:v_cnt > 0) THEN
        EXCEPTION EX_MIGRATION_CHECK 'Phien ban migration 49 da ton tai.';

    SELECT COUNT(*) FROM SCHEMA_MIGRATIONS WHERE VERSION_NO = 48 INTO :v_cnt;
    IF (:v_cnt = 0) THEN
        EXCEPTION EX_MIGRATION_CHECK 'Can migration 48 truoc migration 49.';

    SELECT COUNT(*) FROM RDB$RELATION_FIELDS
    WHERE RDB$RELATION_NAME = 'PRODUCTION_ORDERS' AND RDB$FIELD_NAME = 'SALES_ORDER_ID'
    INTO :v_cnt;
    IF (:v_cnt = 0) THEN
        EXCEPTION EX_MIGRATION_CHECK 'PRODUCTION_ORDERS thieu SALES_ORDER_ID.';
END^
SET TERM ; ^

SET TERM ^ ;
EXECUTE BLOCK AS
DECLARE VARIABLE v_cnt INTEGER;
BEGIN
    /* RDB$NULL_FLAG = 1 means NOT NULL */
    SELECT COUNT(*) FROM RDB$RELATION_FIELDS
    WHERE RDB$RELATION_NAME = 'PRODUCTION_ORDERS'
      AND RDB$FIELD_NAME = 'SALES_ORDER_ID'
      AND RDB$NULL_FLAG = 1
    INTO :v_cnt;
    IF (:v_cnt > 0) THEN
        EXECUTE STATEMENT 'ALTER TABLE PRODUCTION_ORDERS ALTER SALES_ORDER_ID DROP NOT NULL';
END^
SET TERM ; ^

SET TERM ^ ;
EXECUTE BLOCK AS
DECLARE VARIABLE v_cnt INTEGER;
BEGIN
    SELECT COUNT(*) FROM RDB$RELATION_FIELDS
    WHERE RDB$RELATION_NAME = 'PRODUCTION_ORDERS'
      AND RDB$FIELD_NAME = 'SALES_ORDER_ID'
      AND RDB$NULL_FLAG IS NULL
    INTO :v_cnt;
    IF (:v_cnt = 0) THEN
        EXCEPTION EX_MIGRATION_CHECK 'PRODUCTION_ORDERS.SALES_ORDER_ID van NOT NULL.';

    SELECT COUNT(*) FROM RDB$RELATION_CONSTRAINTS
    WHERE RDB$CONSTRAINT_NAME = 'CK_PO_SOURCE'
    INTO :v_cnt;
    IF (:v_cnt = 0) THEN
        EXCEPTION EX_MIGRATION_CHECK 'Thieu CK_PO_SOURCE (STOCK/SALES_ORDER XOR).';
END^
SET TERM ; ^

DROP EXCEPTION EX_MIGRATION_CHECK;
