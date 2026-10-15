/*
================================================================================
KAZUKO ERP JEWELRY - DATABASE MIGRATION SCRIPT
================================================================================
Migration Version: 71
Migration Name:    WORKER_GOLD_DEBT_PER_PURITY
Description:       Change WORKER_GOLD_DEBTS uniqueness from one debt per
                   production order to one debt per (order, gold purity),
                   so an order with multiple gold types (9999, 18K, ...)
                   can record a separate gold debt for each purity.
Target Database:   Firebird 5.0 (Dialect 3, UTF8)
================================================================================
Rollback (dev):
  -- Manual: drop UQ_WGD_ORDER_PURITY, re-add UQ_WGD_ORDER
  DELETE FROM SCHEMA_MIGRATIONS WHERE VERSION_NO = 71;
================================================================================
*/

SET AUTODDL OFF;

CREATE EXCEPTION EX_MIGRATION_CHECK 'Migration 71 validation failed';

SET TERM ^ ;
EXECUTE BLOCK AS
DECLARE VARIABLE v_cnt INTEGER;
BEGIN
    SELECT COUNT(*) FROM SCHEMA_MIGRATIONS WHERE VERSION_NO = 71 INTO :v_cnt;
    IF (:v_cnt > 0) THEN
        EXCEPTION EX_MIGRATION_CHECK 'Phien ban migration 71 da ton tai.';

    SELECT COUNT(*) FROM SCHEMA_MIGRATIONS WHERE VERSION_NO = 70 INTO :v_cnt;
    IF (:v_cnt = 0) THEN
        EXCEPTION EX_MIGRATION_CHECK 'Can migration 70 truoc migration 71.';

    SELECT COUNT(*) FROM RDB$RELATIONS
    WHERE RDB$RELATION_NAME = 'WORKER_GOLD_DEBTS'
    INTO :v_cnt;
    IF (:v_cnt <> 1) THEN
        EXCEPTION EX_MIGRATION_CHECK 'Thieu bang WORKER_GOLD_DEBTS.';

    SELECT COUNT(*) FROM RDB$RELATION_CONSTRAINTS
    WHERE RDB$RELATION_NAME = 'WORKER_GOLD_DEBTS'
      AND RDB$CONSTRAINT_NAME = 'UQ_WGD_ORDER'
    INTO :v_cnt;
    IF (:v_cnt <> 1) THEN
        EXCEPTION EX_MIGRATION_CHECK 'Thieu constraint UQ_WGD_ORDER.';
END^
SET TERM ; ^

ALTER TABLE WORKER_GOLD_DEBTS DROP CONSTRAINT UQ_WGD_ORDER;
ALTER TABLE WORKER_GOLD_DEBTS ADD CONSTRAINT UQ_WGD_ORDER_PURITY
    UNIQUE (COMPANY_ID, PRODUCTION_ORDER_ID, GOLD_PURITY_CODE);

SET TERM ^ ;
EXECUTE BLOCK AS
DECLARE VARIABLE v_cnt INTEGER;
BEGIN
    SELECT COUNT(*) FROM RDB$RELATION_CONSTRAINTS
    WHERE RDB$RELATION_NAME = 'WORKER_GOLD_DEBTS'
      AND RDB$CONSTRAINT_NAME = 'UQ_WGD_ORDER_PURITY'
    INTO :v_cnt;
    IF (:v_cnt <> 1) THEN
        EXCEPTION EX_MIGRATION_CHECK 'Khong tao duoc UQ_WGD_ORDER_PURITY.';
END^
SET TERM ; ^

DROP EXCEPTION EX_MIGRATION_CHECK;
