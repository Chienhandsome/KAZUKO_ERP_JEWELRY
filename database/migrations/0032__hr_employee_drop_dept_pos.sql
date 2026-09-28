/*
================================================================================
KAZUKO ERP JEWELRY - DATABASE MIGRATION SCRIPT
================================================================================
Migration Version: 32
Migration Name:    HR_EMPLOYEE_DROP_DEPT_POS
Description:       Remove DEPARTMENT_ID and POSITION_ID from HR_EMPLOYEES
                   (employee no longer stores department/position).
                   Safe when columns already dropped (e.g. updated 0031).
Target Database:   Firebird 5.0 (Dialect 3, UTF8)

Rollback notes:
  Re-add columns only via a forward-fix migration if business requires;
  prefer restore from pre-32 backup.
================================================================================
*/

SET AUTODDL OFF;

CREATE EXCEPTION EX_MIGRATION_CHECK 'Migration validation error';

SET TERM ^ ;
EXECUTE BLOCK AS
DECLARE VARIABLE v_cnt INTEGER;
BEGIN
    SELECT COUNT(*) FROM SCHEMA_MIGRATIONS WHERE VERSION_NO = 32 INTO :v_cnt;
    IF (:v_cnt > 0) THEN
        EXCEPTION EX_MIGRATION_CHECK 'Phien ban migration 32 da ton tai trong SCHEMA_MIGRATIONS.';

    SELECT COUNT(*) FROM RDB$RELATIONS WHERE RDB$RELATION_NAME = 'HR_EMPLOYEES' INTO :v_cnt;
    IF (:v_cnt = 0) THEN
        EXCEPTION EX_MIGRATION_CHECK 'Bang HR_EMPLOYEES khong ton tai. Hay chay migration 31 truoc.';
END^
SET TERM ; ^

-- Drop FK / indexes / columns only when present (idempotent for updated 0031 installs)
SET TERM ^ ;
EXECUTE BLOCK AS
DECLARE VARIABLE v_cnt INTEGER;
BEGIN
    SELECT COUNT(*) FROM RDB$RELATION_CONSTRAINTS
    WHERE RDB$CONSTRAINT_NAME = 'FK_HR_EMP_DEPARTMENT' INTO :v_cnt;
    IF (:v_cnt > 0) THEN
        EXECUTE STATEMENT 'ALTER TABLE HR_EMPLOYEES DROP CONSTRAINT FK_HR_EMP_DEPARTMENT';

    SELECT COUNT(*) FROM RDB$RELATION_CONSTRAINTS
    WHERE RDB$CONSTRAINT_NAME = 'FK_HR_EMP_POSITION' INTO :v_cnt;
    IF (:v_cnt > 0) THEN
        EXECUTE STATEMENT 'ALTER TABLE HR_EMPLOYEES DROP CONSTRAINT FK_HR_EMP_POSITION';

    SELECT COUNT(*) FROM RDB$INDICES WHERE RDB$INDEX_NAME = 'IX_HR_EMP_DEPARTMENT' INTO :v_cnt;
    IF (:v_cnt > 0) THEN
        EXECUTE STATEMENT 'DROP INDEX IX_HR_EMP_DEPARTMENT';

    SELECT COUNT(*) FROM RDB$INDICES WHERE RDB$INDEX_NAME = 'IX_HR_EMP_POSITION' INTO :v_cnt;
    IF (:v_cnt > 0) THEN
        EXECUTE STATEMENT 'DROP INDEX IX_HR_EMP_POSITION';

    SELECT COUNT(*) FROM RDB$RELATION_FIELDS
    WHERE RDB$RELATION_NAME = 'HR_EMPLOYEES' AND RDB$FIELD_NAME = 'DEPARTMENT_ID' INTO :v_cnt;
    IF (:v_cnt > 0) THEN
        EXECUTE STATEMENT 'ALTER TABLE HR_EMPLOYEES DROP DEPARTMENT_ID';

    SELECT COUNT(*) FROM RDB$RELATION_FIELDS
    WHERE RDB$RELATION_NAME = 'HR_EMPLOYEES' AND RDB$FIELD_NAME = 'POSITION_ID' INTO :v_cnt;
    IF (:v_cnt > 0) THEN
        EXECUTE STATEMENT 'ALTER TABLE HR_EMPLOYEES DROP POSITION_ID';
END^
SET TERM ; ^

SET TERM ^ ;
EXECUTE BLOCK AS
DECLARE VARIABLE v_cnt INTEGER;
BEGIN
    SELECT COUNT(*) FROM RDB$RELATION_FIELDS
    WHERE RDB$RELATION_NAME = 'HR_EMPLOYEES' AND RDB$FIELD_NAME IN ('DEPARTMENT_ID', 'POSITION_ID')
    INTO :v_cnt;
    IF (:v_cnt <> 0) THEN
        EXCEPTION EX_MIGRATION_CHECK 'Chua go duoc DEPARTMENT_ID/POSITION_ID khoi HR_EMPLOYEES.';
END^
SET TERM ; ^

DROP EXCEPTION EX_MIGRATION_CHECK;
