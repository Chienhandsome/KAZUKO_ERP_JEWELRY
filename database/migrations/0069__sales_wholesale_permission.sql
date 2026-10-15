/*
================================================================================
KAZUKO ERP JEWELRY - DATABASE MIGRATION SCRIPT
================================================================================
Migration Version: 69
Migration Name:    SALES_WHOLESALE_PERMISSION
Description:       Seed permission sales.wholesale.post for wholesale sales
                   posting from the Sales module.
Target Database:   Firebird 5.0 (Dialect 3, UTF8)
================================================================================
Rollback (dev):
  DELETE FROM ROLE_PERMISSIONS WHERE PERMISSION_ID IN
    (SELECT ID FROM PERMISSIONS WHERE CODE = 'sales.wholesale.post');
  DELETE FROM PERMISSIONS WHERE CODE = 'sales.wholesale.post';
  DELETE FROM SCHEMA_MIGRATIONS WHERE VERSION_NO = 69;
================================================================================
*/

SET AUTODDL OFF;

CREATE EXCEPTION EX_MIGRATION_CHECK 'Migration 69 validation failed';

SET TERM ^ ;
EXECUTE BLOCK AS
DECLARE VARIABLE v_cnt INTEGER;
BEGIN
    SELECT COUNT(*) FROM SCHEMA_MIGRATIONS WHERE VERSION_NO = 69 INTO :v_cnt;
    IF (:v_cnt > 0) THEN
        EXCEPTION EX_MIGRATION_CHECK 'Phien ban migration 69 da ton tai.';

    SELECT COUNT(*) FROM SCHEMA_MIGRATIONS WHERE VERSION_NO = 68 INTO :v_cnt;
    IF (:v_cnt = 0) THEN
        EXCEPTION EX_MIGRATION_CHECK 'Can migration 68 truoc migration 69.';
END^
SET TERM ; ^

UPDATE OR INSERT INTO PERMISSIONS (CODE, MODULE_CODE, ACTION_CODE, NAME)
VALUES ('sales.wholesale.post', 'SALES', 'WHOLESALE_POST', 'Ghi so ban si tu Ban hang')
MATCHING (CODE);

INSERT INTO ROLE_PERMISSIONS (ROLE_ID, PERMISSION_ID, IS_ALLOWED)
SELECT R.ID, P.ID, TRUE
FROM ROLES R
CROSS JOIN PERMISSIONS P
WHERE R.CODE IN ('ADMIN', 'MANAGER', 'SALE')
  AND P.CODE = 'sales.wholesale.post'
  AND NOT EXISTS (
      SELECT 1 FROM ROLE_PERMISSIONS RP
      WHERE RP.ROLE_ID = R.ID AND RP.PERMISSION_ID = P.ID);

SET TERM ^ ;
EXECUTE BLOCK AS
DECLARE VARIABLE v_cnt INTEGER;
BEGIN
    SELECT COUNT(*) FROM PERMISSIONS WHERE CODE = 'sales.wholesale.post' INTO :v_cnt;
    IF (:v_cnt <> 1) THEN
        EXCEPTION EX_MIGRATION_CHECK 'Khong seed sales.wholesale.post.';
END^
SET TERM ; ^

DROP EXCEPTION EX_MIGRATION_CHECK;
