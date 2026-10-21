/*
================================================================================
KAZUKO ERP JEWELRY - DATABASE MIGRATION SCRIPT
================================================================================
Migration Version: 78
Migration Name:    COMPANY_FACILITY_STANDARD
Description:       Add TCCS (tieu chuan co so) to COMPANIES for jewelry labels.
Target Database:   Firebird 5.0 (Dialect 3, UTF8)
================================================================================
Rollback (dev):
  ALTER TABLE COMPANIES DROP TCCS;
  DELETE FROM SCHEMA_MIGRATIONS WHERE VERSION_NO = 78;
  Preferred recovery is restore from the pre-migration backup.
================================================================================
*/

SET AUTODDL OFF;

CREATE EXCEPTION EX_MIGRATION_CHECK 'Migration 78 validation failed';

SET TERM ^ ;
EXECUTE BLOCK AS
DECLARE VARIABLE v_cnt INTEGER;
BEGIN
    SELECT COUNT(*) FROM SCHEMA_MIGRATIONS WHERE VERSION_NO = 78 INTO :v_cnt;
    IF (:v_cnt > 0) THEN
        EXCEPTION EX_MIGRATION_CHECK 'Phien ban migration 78 da ton tai.';

    SELECT COUNT(*) FROM SCHEMA_MIGRATIONS WHERE VERSION_NO = 77 INTO :v_cnt;
    IF (:v_cnt = 0) THEN
        EXCEPTION EX_MIGRATION_CHECK 'Can migration 77 truoc migration 78.';

    SELECT COUNT(*) FROM RDB$RELATION_FIELDS
    WHERE RDB$RELATION_NAME = 'COMPANIES' AND RDB$FIELD_NAME = 'TCCS'
    INTO :v_cnt;
    IF (:v_cnt > 0) THEN
        EXCEPTION EX_MIGRATION_CHECK 'Cot TCCS da ton tai tren COMPANIES.';
END^
SET TERM ; ^

ALTER TABLE COMPANIES ADD TCCS VARCHAR(100);

DROP EXCEPTION EX_MIGRATION_CHECK;
