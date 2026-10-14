/*
================================================================================
KAZUKO ERP JEWELRY - DATABASE MIGRATION SCRIPT
================================================================================
Migration Version: 64
Migration Name:    SALES_INTAKE_APPROVAL
Description:       TP→BH transfer becomes draft request; BH approves/rejects.
                   Adds STOCK_DOCUMENTS.CANCELLATION_REASON if missing and
                   permission warehouse.sales_intake.approve.
Target Database:   Firebird 5.0 (Dialect 3, UTF8)
================================================================================
Rollback (dev):
  DELETE FROM ROLE_PERMISSIONS WHERE PERMISSION_ID IN
    (SELECT ID FROM PERMISSIONS WHERE CODE = 'warehouse.sales_intake.approve');
  DELETE FROM PERMISSIONS WHERE CODE = 'warehouse.sales_intake.approve';
  -- CANCELLATION_REASON column: keep (shared with other flows)
  DELETE FROM SCHEMA_MIGRATIONS WHERE VERSION_NO = 64;
================================================================================
*/

SET AUTODDL OFF;

CREATE EXCEPTION EX_MIGRATION_CHECK 'Migration validation error';

SET TERM ^ ;
EXECUTE BLOCK AS
DECLARE VARIABLE v_cnt INTEGER;
BEGIN
    SELECT COUNT(*) FROM SCHEMA_MIGRATIONS WHERE VERSION_NO = 64 INTO :v_cnt;
    IF (:v_cnt > 0) THEN
        EXCEPTION EX_MIGRATION_CHECK 'Phien ban migration 64 da ton tai.';

    SELECT COUNT(*) FROM SCHEMA_MIGRATIONS WHERE VERSION_NO = 63 INTO :v_cnt;
    IF (:v_cnt = 0) THEN
        EXCEPTION EX_MIGRATION_CHECK 'Can migration 63 truoc migration 64.';
END^
SET TERM ; ^

/* ---- Cancellation reason on stock documents (idempotent) ---- */
SET TERM ^ ;
EXECUTE BLOCK AS
DECLARE VARIABLE v_cnt INTEGER;
BEGIN
    SELECT COUNT(*)
    FROM RDB$RELATION_FIELDS
    WHERE RDB$RELATION_NAME = 'STOCK_DOCUMENTS'
      AND RDB$FIELD_NAME = 'CANCELLATION_REASON'
    INTO :v_cnt;
    IF (:v_cnt = 0) THEN
        EXECUTE STATEMENT
            'ALTER TABLE STOCK_DOCUMENTS ADD CANCELLATION_REASON VARCHAR(500)';
END^
SET TERM ; ^

/* ---- Permission for BH approve/reject ---- */
UPDATE OR INSERT INTO PERMISSIONS (CODE, MODULE_CODE, ACTION_CODE, NAME)
VALUES ('warehouse.sales_intake.approve', 'WAREHOUSE', 'SALES_INTAKE_APPROVE',
        'Duyet / tu choi yeu cau nhap thanh pham vao Kho ban hang')
MATCHING (CODE);

INSERT INTO ROLE_PERMISSIONS (ROLE_ID, PERMISSION_ID, IS_ALLOWED)
SELECT R.ID, P.ID, TRUE
FROM ROLES R
CROSS JOIN PERMISSIONS P
WHERE R.CODE IN ('ADMIN', 'MANAGER')
  AND P.CODE = 'warehouse.sales_intake.approve'
  AND NOT EXISTS (
      SELECT 1 FROM ROLE_PERMISSIONS RP
      WHERE RP.ROLE_ID = R.ID AND RP.PERMISSION_ID = P.ID);

SET TERM ^ ;
EXECUTE BLOCK AS
DECLARE VARIABLE v_cnt INTEGER;
BEGIN
    SELECT COUNT(*) FROM RDB$INDICES WHERE RDB$INDEX_NAME = 'IX_STOCK_DOC_SALES_INTAKE' INTO :v_cnt;
    IF (:v_cnt = 0) THEN
        EXECUTE STATEMENT
            'CREATE INDEX IX_STOCK_DOC_SALES_INTAKE ON STOCK_DOCUMENTS (BRANCH_ID, DOCUMENT_TYPE, PURPOSE_CODE, STATUS_CODE)';
END^
SET TERM ; ^

SET TERM ^ ;
EXECUTE BLOCK AS
DECLARE VARIABLE v_cnt INTEGER;
BEGIN
    SELECT COUNT(*)
    FROM RDB$RELATION_FIELDS
    WHERE RDB$RELATION_NAME = 'STOCK_DOCUMENTS'
      AND RDB$FIELD_NAME = 'CANCELLATION_REASON'
    INTO :v_cnt;
    IF (:v_cnt = 0) THEN
        EXCEPTION EX_MIGRATION_CHECK 'Thieu STOCK_DOCUMENTS.CANCELLATION_REASON.';

    SELECT COUNT(*) FROM PERMISSIONS WHERE CODE = 'warehouse.sales_intake.approve' INTO :v_cnt;
    IF (:v_cnt <> 1) THEN
        EXCEPTION EX_MIGRATION_CHECK 'Khong seed warehouse.sales_intake.approve.';

    SELECT COUNT(*) FROM RDB$INDICES WHERE RDB$INDEX_NAME = 'IX_STOCK_DOC_SALES_INTAKE' INTO :v_cnt;
    IF (:v_cnt <> 1) THEN
        EXCEPTION EX_MIGRATION_CHECK 'Khong tao IX_STOCK_DOC_SALES_INTAKE.';
END^
SET TERM ; ^

DROP EXCEPTION EX_MIGRATION_CHECK;
