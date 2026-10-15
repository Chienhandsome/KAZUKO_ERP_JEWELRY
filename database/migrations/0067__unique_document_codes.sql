/*
================================================================================
KAZUKO ERP JEWELRY - DATABASE MIGRATION SCRIPT
================================================================================
Migration Version: 67
Migration Name:    UNIQUE_DOCUMENT_CODES
Description:       Add UNIQUE constraints on PRODUCTION_ORDERS.CODE and
                   STOCK_DOCUMENTS.CODE as a database-level backstop against
                   duplicate document codes (application already uses
                   DOCUMENT_SEQUENCES row-lock, this is defense in depth).
Target Database:   Firebird 5.0 (Dialect 3, UTF8)
================================================================================
Rollback (dev):
  ALTER TABLE PRODUCTION_ORDERS DROP CONSTRAINT UQ_PRODUCTION_ORDERS_CODE;
  ALTER TABLE STOCK_DOCUMENTS DROP CONSTRAINT UQ_STOCK_DOCUMENTS_CODE;
  DELETE FROM SCHEMA_MIGRATIONS WHERE VERSION_NO = 67;
================================================================================
*/

SET AUTODDL OFF;

CREATE EXCEPTION EX_MIGRATION_CHECK 'Migration 67 validation failed';

SET TERM ^ ;
EXECUTE BLOCK AS
DECLARE VARIABLE v_cnt INTEGER;
BEGIN
    SELECT COUNT(*) FROM SCHEMA_MIGRATIONS WHERE VERSION_NO = 67 INTO :v_cnt;
    IF (:v_cnt > 0) THEN
        EXCEPTION EX_MIGRATION_CHECK 'Phien ban migration 67 da ton tai.';

    SELECT COUNT(*) FROM SCHEMA_MIGRATIONS WHERE VERSION_NO = 66 INTO :v_cnt;
    IF (:v_cnt = 0) THEN
        EXCEPTION EX_MIGRATION_CHECK 'Can migration 66 truoc migration 67.';

    -- Fail fast if duplicate codes already exist
    SELECT COUNT(*) FROM (
        SELECT CODE FROM PRODUCTION_ORDERS GROUP BY CODE HAVING COUNT(*) > 1
    ) INTO :v_cnt;
    IF (:v_cnt > 0) THEN
        EXCEPTION EX_MIGRATION_CHECK 'Ton tai ma lenh san xuat trung lap.';

    SELECT COUNT(*) FROM (
        SELECT CODE FROM STOCK_DOCUMENTS GROUP BY CODE HAVING COUNT(*) > 1
    ) INTO :v_cnt;
    IF (:v_cnt > 0) THEN
        EXCEPTION EX_MIGRATION_CHECK 'Ton tai ma chung tu kho trung lap.';
END^
SET TERM ; ^

ALTER TABLE PRODUCTION_ORDERS ADD CONSTRAINT UQ_PRODUCTION_ORDERS_CODE UNIQUE (CODE);
ALTER TABLE STOCK_DOCUMENTS ADD CONSTRAINT UQ_STOCK_DOCUMENTS_CODE UNIQUE (CODE);

SET TERM ^ ;
EXECUTE BLOCK AS
DECLARE VARIABLE v_cnt INTEGER;
BEGIN
    SELECT COUNT(*) FROM RDB$RELATION_CONSTRAINTS
    WHERE RDB$RELATION_NAME = 'PRODUCTION_ORDERS'
      AND RDB$CONSTRAINT_NAME = 'UQ_PRODUCTION_ORDERS_CODE'
    INTO :v_cnt;
    IF (:v_cnt <> 1) THEN
        EXCEPTION EX_MIGRATION_CHECK 'Khong tao duoc UQ_PRODUCTION_ORDERS_CODE.';

    SELECT COUNT(*) FROM RDB$RELATION_CONSTRAINTS
    WHERE RDB$RELATION_NAME = 'STOCK_DOCUMENTS'
      AND RDB$CONSTRAINT_NAME = 'UQ_STOCK_DOCUMENTS_CODE'
    INTO :v_cnt;
    IF (:v_cnt <> 1) THEN
        EXCEPTION EX_MIGRATION_CHECK 'Khong tao duoc UQ_STOCK_DOCUMENTS_CODE.';
END^
SET TERM ; ^

DROP EXCEPTION EX_MIGRATION_CHECK;
