/*
================================================================================
KAZUKO ERP JEWELRY - DATABASE MIGRATION SCRIPT
================================================================================
Migration Version: 68
Migration Name:    WORKER_GOLD_DEBT_EXTERNAL_WORKER
Description:       Allow WORKER_GOLD_DEBTS.WORKER_TYPE = 'EXTERNAL_WORKER' in
                   addition to 'EMPLOYEE', so gold debts can be recorded for
                   outsourced workers (thợ ngoài) as well as internal workers.
Target Database:   Firebird 5.0 (Dialect 3, UTF8)
================================================================================
Rollback (dev):
  -- Manual: drop and re-add constraint with only 'EMPLOYEE'
  DELETE FROM SCHEMA_MIGRATIONS WHERE VERSION_NO = 68;
================================================================================
*/

SET AUTODDL OFF;

CREATE EXCEPTION EX_MIGRATION_CHECK 'Migration 68 validation failed';

SET TERM ^ ;
EXECUTE BLOCK AS
DECLARE VARIABLE v_cnt INTEGER;
BEGIN
    SELECT COUNT(*) FROM SCHEMA_MIGRATIONS WHERE VERSION_NO = 68 INTO :v_cnt;
    IF (:v_cnt > 0) THEN
        EXCEPTION EX_MIGRATION_CHECK 'Phien ban migration 68 da ton tai.';

    SELECT COUNT(*) FROM SCHEMA_MIGRATIONS WHERE VERSION_NO = 67 INTO :v_cnt;
    IF (:v_cnt = 0) THEN
        EXCEPTION EX_MIGRATION_CHECK 'Can migration 67 truoc migration 68.';

    SELECT COUNT(*) FROM RDB$RELATIONS
    WHERE RDB$RELATION_NAME = 'WORKER_GOLD_DEBTS'
    INTO :v_cnt;
    IF (:v_cnt <> 1) THEN
        EXCEPTION EX_MIGRATION_CHECK 'Thieu bang WORKER_GOLD_DEBTS.';
END^
SET TERM ; ^

ALTER TABLE WORKER_GOLD_DEBTS DROP CONSTRAINT CK_WGD_WORKER_TYPE;
ALTER TABLE WORKER_GOLD_DEBTS ADD CONSTRAINT CK_WGD_WORKER_TYPE CHECK (WORKER_TYPE IN ('EMPLOYEE', 'EXTERNAL_WORKER'));

SET TERM ^ ;
EXECUTE BLOCK AS
DECLARE VARIABLE v_cnt INTEGER;
BEGIN
    SELECT COUNT(*) FROM RDB$RELATION_CONSTRAINTS
    WHERE RDB$RELATION_NAME = 'WORKER_GOLD_DEBTS'
      AND RDB$CONSTRAINT_NAME = 'CK_WGD_WORKER_TYPE'
    INTO :v_cnt;
    IF (:v_cnt <> 1) THEN
        EXCEPTION EX_MIGRATION_CHECK 'Khong tao duoc CK_WGD_WORKER_TYPE.';
END^
SET TERM ; ^

DROP EXCEPTION EX_MIGRATION_CHECK;
