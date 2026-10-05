/*
================================================================================
KAZUKO ERP JEWELRY - DATABASE MIGRATION SCRIPT
================================================================================
Migration Version: 48
Migration Name:    PRODUCTION_ATTACHMENT_LINE_NUMBER
Description:       Link PRODUCTION_ATTACHMENTS to sample lines via LINE_NUMBER
                   (stable across draft line replace). Optional null for legacy
                   order-level attachments.
Target Database:   Firebird 5.0 (Dialect 3, UTF8)

Rollback notes (manual, forward-only ledger):
  1. Backup with gbak before any DROP.
  2. Drop index IX_PA_LINE; drop column LINE_NUMBER.
  3. Preferred recovery in production is restore from pre-migration .fbk.
================================================================================
*/

SET AUTODDL OFF;

CREATE EXCEPTION EX_MIGRATION_CHECK 'Migration validation error';

SET TERM ^ ;
EXECUTE BLOCK AS
DECLARE VARIABLE v_cnt INTEGER;
BEGIN
    SELECT COUNT(*) FROM SCHEMA_MIGRATIONS WHERE VERSION_NO = 48 INTO :v_cnt;
    IF (:v_cnt > 0) THEN
        EXCEPTION EX_MIGRATION_CHECK 'Phien ban migration 48 da ton tai.';

    SELECT COUNT(*) FROM SCHEMA_MIGRATIONS WHERE VERSION_NO = 47 INTO :v_cnt;
    IF (:v_cnt = 0) THEN
        EXCEPTION EX_MIGRATION_CHECK 'Can migration 47 truoc migration 48.';

    SELECT COUNT(*) FROM RDB$RELATIONS WHERE RDB$RELATION_NAME = 'PRODUCTION_ATTACHMENTS' INTO :v_cnt;
    IF (:v_cnt = 0) THEN
        EXCEPTION EX_MIGRATION_CHECK 'Bang PRODUCTION_ATTACHMENTS khong ton tai.';
END^
SET TERM ; ^

SET TERM ^ ;
EXECUTE BLOCK AS
DECLARE VARIABLE v_cnt INTEGER;
BEGIN
    SELECT COUNT(*) FROM RDB$RELATION_FIELDS
    WHERE RDB$RELATION_NAME = 'PRODUCTION_ATTACHMENTS' AND RDB$FIELD_NAME = 'LINE_NUMBER'
    INTO :v_cnt;
    IF (:v_cnt = 0) THEN
        EXECUTE STATEMENT 'ALTER TABLE PRODUCTION_ATTACHMENTS ADD LINE_NUMBER INTEGER';
END^
SET TERM ; ^

SET TERM ^ ;
EXECUTE BLOCK AS
DECLARE VARIABLE v_cnt INTEGER;
BEGIN
    SELECT COUNT(*) FROM RDB$RELATION_CONSTRAINTS
    WHERE RDB$CONSTRAINT_NAME = 'CK_PA_LINE_NO'
    INTO :v_cnt;
    IF (:v_cnt = 0) THEN
        EXECUTE STATEMENT
            'ALTER TABLE PRODUCTION_ATTACHMENTS ADD CONSTRAINT CK_PA_LINE_NO '
            || 'CHECK (LINE_NUMBER IS NULL OR LINE_NUMBER > 0)';
END^
SET TERM ; ^

SET TERM ^ ;
EXECUTE BLOCK AS
DECLARE VARIABLE v_cnt INTEGER;
BEGIN
    SELECT COUNT(*) FROM RDB$INDICES WHERE RDB$INDEX_NAME = 'IX_PA_LINE' INTO :v_cnt;
    IF (:v_cnt = 0) THEN
        EXECUTE STATEMENT
            'CREATE INDEX IX_PA_LINE ON PRODUCTION_ATTACHMENTS (PRODUCTION_ORDER_ID, LINE_NUMBER, DISPLAY_ORDER)';
END^
SET TERM ; ^

SET TERM ^ ;
EXECUTE BLOCK AS
DECLARE VARIABLE v_cnt INTEGER;
BEGIN
    SELECT COUNT(*) FROM RDB$RELATION_FIELDS
    WHERE RDB$RELATION_NAME = 'PRODUCTION_ATTACHMENTS' AND RDB$FIELD_NAME = 'LINE_NUMBER'
    INTO :v_cnt;
    IF (:v_cnt = 0) THEN
        EXCEPTION EX_MIGRATION_CHECK 'PRODUCTION_ATTACHMENTS thieu LINE_NUMBER.';

    SELECT COUNT(*) FROM RDB$INDICES WHERE RDB$INDEX_NAME = 'IX_PA_LINE' INTO :v_cnt;
    IF (:v_cnt = 0) THEN
        EXCEPTION EX_MIGRATION_CHECK 'Thieu IX_PA_LINE.';
END^
SET TERM ; ^

DROP EXCEPTION EX_MIGRATION_CHECK;
