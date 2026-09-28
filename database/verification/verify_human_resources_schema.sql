/*
================================================================================
KAZUKO ERP JEWELRY - VERIFICATION SCRIPT FOR HUMAN RESOURCES SCHEMA
================================================================================
Target Database: data\data.fdb
Purpose:         Fail-fast verification for HR_DEPARTMENTS, HR_POSITIONS,
                 HR_EMPLOYEES, constraints, indexes and seed counts.
Note:            Update CHECKSUM_VALUE after applying migration 31 via runner
                 (full artifact SHA-256 of 0031__human_resources_core.sql).
================================================================================
*/

SET AUTODDL OFF;

CREATE EXCEPTION EX_VERIFY_FAILED 'Schema verification failed';

SET TERM ^ ;
EXECUTE BLOCK AS
DECLARE VARIABLE v_cnt INTEGER;
BEGIN
    SELECT COUNT(*) FROM SCHEMA_MIGRATIONS WHERE VERSION_NO = 31 INTO :v_cnt;
    IF (:v_cnt <> 1) THEN
        EXCEPTION EX_VERIFY_FAILED 'SCHEMA_MIGRATIONS thieu ban ghi phien ban 31.';

    SELECT COUNT(*) FROM RDB$RELATIONS WHERE RDB$SYSTEM_FLAG = 0 AND RDB$RELATION_NAME = 'HR_DEPARTMENTS' INTO :v_cnt;
    IF (:v_cnt <> 1) THEN
        EXCEPTION EX_VERIFY_FAILED 'Bang HR_DEPARTMENTS khong ton tai.';

    SELECT COUNT(*) FROM RDB$RELATIONS WHERE RDB$SYSTEM_FLAG = 0 AND RDB$RELATION_NAME = 'HR_POSITIONS' INTO :v_cnt;
    IF (:v_cnt <> 1) THEN
        EXCEPTION EX_VERIFY_FAILED 'Bang HR_POSITIONS khong ton tai.';

    SELECT COUNT(*) FROM RDB$RELATIONS WHERE RDB$SYSTEM_FLAG = 0 AND RDB$RELATION_NAME = 'HR_EMPLOYEES' INTO :v_cnt;
    IF (:v_cnt <> 1) THEN
        EXCEPTION EX_VERIFY_FAILED 'Bang HR_EMPLOYEES khong ton tai.';

    SELECT COUNT(*) FROM RDB$RELATION_FIELDS
    WHERE RDB$RELATION_NAME = 'HR_EMPLOYEES'
      AND RDB$FIELD_NAME IN ('ID', 'COMPANY_ID', 'CODE', 'FULL_NAME',
                             'BASE_SALARY', 'ALLOWANCE', 'STATUS_CODE', 'IS_ACTIVE', 'CREATED_AT')
      AND RDB$NULL_FLAG = 1
    INTO :v_cnt;
    IF (:v_cnt <> 9) THEN
        EXCEPTION EX_VERIFY_FAILED 'Nullability hoac thieu cot bat buoc trong HR_EMPLOYEES.';

    SELECT COUNT(*) FROM RDB$RELATION_FIELDS
    WHERE RDB$RELATION_NAME = 'HR_EMPLOYEES'
      AND RDB$FIELD_NAME IN ('DEPARTMENT_ID', 'POSITION_ID')
    INTO :v_cnt;
    IF (:v_cnt <> 0) THEN
        EXCEPTION EX_VERIFY_FAILED 'HR_EMPLOYEES van con cot DEPARTMENT_ID/POSITION_ID.';

    SELECT COUNT(*) FROM RDB$RELATION_CONSTRAINTS
    WHERE RDB$CONSTRAINT_NAME IN ('PK_HR_DEPARTMENTS', 'PK_HR_POSITIONS', 'PK_HR_EMPLOYEES')
      AND RDB$CONSTRAINT_TYPE = 'PRIMARY KEY'
    INTO :v_cnt;
    IF (:v_cnt <> 3) THEN
        EXCEPTION EX_VERIFY_FAILED 'Thieu khoa chinh HR.';

    SELECT COUNT(*) FROM RDB$RELATION_CONSTRAINTS
    WHERE RDB$CONSTRAINT_NAME IN (
        'UQ_HR_DEPT_CODE', 'UQ_HR_DEPT_COMP_ID',
        'UQ_HR_POS_CODE', 'UQ_HR_POS_COMP_ID',
        'UQ_HR_EMP_CODE', 'UQ_HR_EMP_COMP_ID')
      AND RDB$CONSTRAINT_TYPE = 'UNIQUE'
    INTO :v_cnt;
    IF (:v_cnt <> 6) THEN
        EXCEPTION EX_VERIFY_FAILED 'Thieu unique constraint HR.';

    SELECT COUNT(*) FROM RDB$RELATION_CONSTRAINTS
    WHERE RDB$CONSTRAINT_NAME IN (
        'FK_HR_DEPT_COMPANY', 'FK_HR_POS_COMPANY', 'FK_HR_EMP_COMPANY', 'FK_HR_EMP_BRANCH')
      AND RDB$CONSTRAINT_TYPE = 'FOREIGN KEY'
    INTO :v_cnt;
    IF (:v_cnt <> 4) THEN
        EXCEPTION EX_VERIFY_FAILED 'Thieu khoa ngoai HR.';

    SELECT COUNT(*) FROM HR_DEPARTMENTS INTO :v_cnt;
    IF (:v_cnt < 5) THEN
        EXCEPTION EX_VERIFY_FAILED 'Seed phong ban thieu (can it nhat 5).';

    SELECT COUNT(*) FROM HR_POSITIONS INTO :v_cnt;
    IF (:v_cnt < 6) THEN
        EXCEPTION EX_VERIFY_FAILED 'Seed chuc vu thieu (can it nhat 6).';
END^
SET TERM ; ^

DROP EXCEPTION EX_VERIFY_FAILED;
