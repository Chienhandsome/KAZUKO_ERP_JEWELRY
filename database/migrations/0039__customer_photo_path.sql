/*
================================================================================
KAZUKO ERP JEWELRY - DATABASE MIGRATION SCRIPT
================================================================================
Migration Version: 39
Migration Name:    CUSTOMER_PHOTO_PATH
Description:       Add PHOTO_PATH to CUSTOMERS for customer profile photos
                   (same pattern as HR_EMPLOYEES.PHOTO_PATH).
Target Database:   Firebird 5.0 (Dialect 3, UTF8)

Rollback notes (manual, forward-only ledger):
  1. Backup with gbak before DROP.
  2. ALTER TABLE CUSTOMERS DROP PHOTO_PATH;
  3. Preferred recovery is restore from pre-migration .fbk.
================================================================================
*/

SET AUTODDL OFF;

CREATE EXCEPTION EX_MIGRATION_CHECK 'Migration validation error';

SET TERM ^ ;
EXECUTE BLOCK AS
DECLARE VARIABLE v_cnt INTEGER;
BEGIN
    SELECT COUNT(*) FROM SCHEMA_MIGRATIONS WHERE VERSION_NO = 39 INTO :v_cnt;
    IF (:v_cnt > 0) THEN
        EXCEPTION EX_MIGRATION_CHECK 'Phien ban migration 39 da ton tai trong SCHEMA_MIGRATIONS.';

    SELECT COUNT(*) FROM RDB$RELATIONS WHERE RDB$RELATION_NAME = 'CUSTOMERS' INTO :v_cnt;
    IF (:v_cnt = 0) THEN
        EXCEPTION EX_MIGRATION_CHECK 'Bang CUSTOMERS khong ton tai.';

    SELECT COUNT(*) FROM RDB$RELATION_FIELDS
    WHERE RDB$RELATION_NAME = 'CUSTOMERS' AND RDB$FIELD_NAME = 'PHOTO_PATH'
    INTO :v_cnt;
    IF (:v_cnt > 0) THEN
        EXCEPTION EX_MIGRATION_CHECK 'Cot PHOTO_PATH da ton tai tren CUSTOMERS.';
END^
SET TERM ; ^

ALTER TABLE CUSTOMERS ADD PHOTO_PATH VARCHAR(500);

SET TERM ^ ;
EXECUTE BLOCK AS
DECLARE VARIABLE v_cnt INTEGER;
BEGIN
    SELECT COUNT(*) FROM RDB$RELATION_FIELDS
    WHERE RDB$RELATION_NAME = 'CUSTOMERS' AND RDB$FIELD_NAME = 'PHOTO_PATH'
    INTO :v_cnt;
    IF (:v_cnt = 0) THEN
        EXCEPTION EX_MIGRATION_CHECK 'Khong tao duoc cot PHOTO_PATH tren CUSTOMERS.';
END^
SET TERM ; ^

DROP EXCEPTION EX_MIGRATION_CHECK;
