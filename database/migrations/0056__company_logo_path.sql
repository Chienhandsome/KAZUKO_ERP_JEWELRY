/*
================================================================================
KAZUKO ERP JEWELRY - DATABASE MIGRATION SCRIPT
================================================================================
Migration Version: 56
Migration Name:    COMPANY_LOGO_PATH
Description:       Add LOGO_PATH to COMPANIES for company logo in settings.
Target Database:   Firebird 5.0 (Dialect 3, UTF8)

Rollback notes (manual, forward-only ledger):
  1. Backup with gbak before DROP.
  2. ALTER TABLE COMPANIES DROP LOGO_PATH;
  3. Preferred recovery is restore from pre-migration .fbk.
================================================================================
*/

SET AUTODDL OFF;

CREATE EXCEPTION EX_MIGRATION_CHECK 'Migration validation error';

SET TERM ^ ;
EXECUTE BLOCK AS
DECLARE VARIABLE v_cnt INTEGER;
BEGIN
    SELECT COUNT(*) FROM SCHEMA_MIGRATIONS WHERE VERSION_NO = 56 INTO :v_cnt;
    IF (:v_cnt > 0) THEN
        EXCEPTION EX_MIGRATION_CHECK 'Phien ban migration 56 da ton tai trong SCHEMA_MIGRATIONS.';

    SELECT COUNT(*) FROM RDB$RELATIONS WHERE RDB$RELATION_NAME = 'COMPANIES' INTO :v_cnt;
    IF (:v_cnt = 0) THEN
        EXCEPTION EX_MIGRATION_CHECK 'Bang COMPANIES khong ton tai.';

    SELECT COUNT(*) FROM RDB$RELATION_FIELDS
    WHERE RDB$RELATION_NAME = 'COMPANIES' AND RDB$FIELD_NAME = 'LOGO_PATH'
    INTO :v_cnt;
    IF (:v_cnt > 0) THEN
        EXCEPTION EX_MIGRATION_CHECK 'Cot LOGO_PATH da ton tai tren COMPANIES.';
END^
SET TERM ; ^

ALTER TABLE COMPANIES ADD LOGO_PATH VARCHAR(500);

SET TERM ^ ;
EXECUTE BLOCK AS
DECLARE VARIABLE v_cnt INTEGER;
BEGIN
    SELECT COUNT(*) FROM RDB$RELATION_FIELDS
    WHERE RDB$RELATION_NAME = 'COMPANIES' AND RDB$FIELD_NAME = 'LOGO_PATH'
    INTO :v_cnt;
    IF (:v_cnt = 0) THEN
        EXCEPTION EX_MIGRATION_CHECK 'Khong tao duoc cot LOGO_PATH tren COMPANIES.';
END^
SET TERM ; ^

DROP EXCEPTION EX_MIGRATION_CHECK;
