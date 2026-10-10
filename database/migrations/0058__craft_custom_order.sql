/*
================================================================================
KAZUKO ERP JEWELRY - DATABASE MIGRATION SCRIPT
================================================================================
Migration Version: 58
Migration Name:    CRAFT_CUSTOM_ORDER
Description:       Sequence + permission + index for CUSTOM craft orders
                   (đơn đặt chế tác) with mandatory deposit posting.
Target Database:   Firebird 5.0 (Dialect 3, UTF8)

Rollback notes (manual, forward-only ledger):
  1. Backup with gbak before DROP.
  2. DELETE ROLE_PERMISSIONS / PERMISSIONS for sales.craft_order.create;
     DROP INDEX IX_SO_CRAFT_PICKER; DELETE DOCUMENT_SEQUENCES CRAFT_ORDER.
  3. Preferred recovery is restore from pre-migration .fbk.
================================================================================
*/

SET AUTODDL OFF;

CREATE EXCEPTION EX_MIGRATION_CHECK 'Migration validation error';

SET TERM ^ ;
EXECUTE BLOCK AS
DECLARE VARIABLE v_cnt INTEGER;
BEGIN
    SELECT COUNT(*) FROM SCHEMA_MIGRATIONS WHERE VERSION_NO = 58 INTO :v_cnt;
    IF (:v_cnt > 0) THEN
        EXCEPTION EX_MIGRATION_CHECK 'Phien ban migration 58 da ton tai trong SCHEMA_MIGRATIONS.';

    SELECT COUNT(*) FROM SCHEMA_MIGRATIONS WHERE VERSION_NO = 57 INTO :v_cnt;
    IF (:v_cnt = 0) THEN
        EXCEPTION EX_MIGRATION_CHECK 'Can chay migration 57 truoc.';

    SELECT COUNT(*) FROM RDB$RELATIONS WHERE RDB$RELATION_NAME = 'SALES_ORDERS' INTO :v_cnt;
    IF (:v_cnt = 0) THEN
        EXCEPTION EX_MIGRATION_CHECK 'Thieu bang SALES_ORDERS.';

    SELECT COUNT(*) FROM RDB$RELATIONS WHERE RDB$RELATION_NAME = 'ORDER_PAYMENTS' INTO :v_cnt;
    IF (:v_cnt = 0) THEN
        EXCEPTION EX_MIGRATION_CHECK 'Thieu bang ORDER_PAYMENTS.';
END^
SET TERM ; ^

/* ---- Sequence for craft / custom order codes (prefix DH) ---- */
INSERT INTO DOCUMENT_SEQUENCES
    (DOCUMENT_TYPE, BRANCH_ID, YEAR_NO, LAST_NUMBER, UPDATED_AT)
SELECT 'CRAFT_ORDER', B.ID, EXTRACT(YEAR FROM CURRENT_DATE), 0, CURRENT_TIMESTAMP
FROM BRANCHES B
WHERE NOT EXISTS (
    SELECT 1 FROM DOCUMENT_SEQUENCES S
    WHERE S.DOCUMENT_TYPE = 'CRAFT_ORDER'
      AND S.BRANCH_ID = B.ID
      AND S.YEAR_NO = EXTRACT(YEAR FROM CURRENT_DATE));

/* ---- Picker index for CUSTOM orders by branch/status ---- */
SET TERM ^ ;
EXECUTE BLOCK AS
DECLARE VARIABLE v_cnt INTEGER;
BEGIN
    SELECT COUNT(*) FROM RDB$INDICES WHERE RDB$INDEX_NAME = 'IX_SO_CRAFT_PICKER' INTO :v_cnt;
    IF (:v_cnt = 0) THEN
        EXECUTE STATEMENT
            'CREATE INDEX IX_SO_CRAFT_PICKER ON SALES_ORDERS (COMPANY_ID, BRANCH_ID, ORDER_TYPE, STATUS_CODE)';
END^
SET TERM ; ^

/* ---- Permission ---- */
UPDATE OR INSERT INTO PERMISSIONS (CODE, MODULE_CODE, ACTION_CODE, NAME)
VALUES ('sales.craft_order.create', 'SALES', 'CRAFT_ORDER_CREATE', 'Tao don dat che tac va thu coc')
MATCHING (CODE);

INSERT INTO ROLE_PERMISSIONS (ROLE_ID, PERMISSION_ID, IS_ALLOWED)
SELECT R.ID, P.ID, TRUE
FROM ROLES R
CROSS JOIN PERMISSIONS P
WHERE R.CODE IN ('ADMIN', 'MANAGER', 'SALE')
  AND P.CODE = 'sales.craft_order.create'
  AND NOT EXISTS (
      SELECT 1 FROM ROLE_PERMISSIONS RP
      WHERE RP.ROLE_ID = R.ID AND RP.PERMISSION_ID = P.ID);

SET TERM ^ ;
EXECUTE BLOCK AS
DECLARE VARIABLE v_cnt INTEGER;
BEGIN
    SELECT COUNT(*) FROM DOCUMENT_SEQUENCES
    WHERE DOCUMENT_TYPE = 'CRAFT_ORDER'
    INTO :v_cnt;
    IF (:v_cnt = 0) THEN
        EXCEPTION EX_MIGRATION_CHECK 'Khong seed DOCUMENT_SEQUENCES CRAFT_ORDER.';

    SELECT COUNT(*) FROM RDB$INDICES WHERE RDB$INDEX_NAME = 'IX_SO_CRAFT_PICKER' INTO :v_cnt;
    IF (:v_cnt <> 1) THEN
        EXCEPTION EX_MIGRATION_CHECK 'Khong tao IX_SO_CRAFT_PICKER.';

    SELECT COUNT(*) FROM PERMISSIONS WHERE CODE = 'sales.craft_order.create' INTO :v_cnt;
    IF (:v_cnt <> 1) THEN
        EXCEPTION EX_MIGRATION_CHECK 'Khong seed sales.craft_order.create.';
END^
SET TERM ; ^

DROP EXCEPTION EX_MIGRATION_CHECK;
