/*
================================================================================
KAZUKO ERP JEWELRY - DATABASE MIGRATION SCRIPT
================================================================================
Migration Version: 63
Migration Name:    DROP_PRODUCTION_ORDER_DELIVERY_FIELDS
Description:       Drop DELIVERED_BY_NAME and RECEIVED_BY_NAME from
                   PRODUCTION_ORDERS. The deliverer/receiver fields were
                   removed from the production order dialog per business
                   decision (no longer tracked at order header level).
                   STOCK_DOCUMENTS keeps its own DELIVERED_BY_NAME /
                   RECEIVED_BY_NAME columns for material issue and return
                   documents.
Target Database:   Firebird 5.0 (Dialect 3, UTF8)
================================================================================
*/

SET AUTODDL OFF;

CREATE EXCEPTION EX_MIGRATION_CHECK 'Migration 63 validation failed';

SET TERM ^ ;
EXECUTE BLOCK AS
DECLARE VARIABLE v_cnt INTEGER;
BEGIN
    SELECT COUNT(*) FROM SCHEMA_MIGRATIONS WHERE VERSION_NO = 63 INTO :v_cnt;
    IF (:v_cnt > 0) THEN
        EXCEPTION EX_MIGRATION_CHECK 'Phien ban migration 63 da ton tai.';

    SELECT COUNT(*) FROM SCHEMA_MIGRATIONS WHERE VERSION_NO = 62 INTO :v_cnt;
    IF (:v_cnt <> 1) THEN
        EXCEPTION EX_MIGRATION_CHECK 'Can migration 62 truoc migration 63.';

    SELECT COUNT(*) FROM RDB$RELATIONS WHERE RDB$RELATION_NAME = 'PRODUCTION_ORDERS' INTO :v_cnt;
    IF (:v_cnt <> 1) THEN
        EXCEPTION EX_MIGRATION_CHECK 'Thieu bang PRODUCTION_ORDERS.';

    SELECT COUNT(*) FROM RDB$RELATION_FIELDS
    WHERE RDB$RELATION_NAME = 'PRODUCTION_ORDERS' AND RDB$FIELD_NAME = 'DELIVERED_BY_NAME' INTO :v_cnt;
    IF (:v_cnt <> 1) THEN
        EXCEPTION EX_MIGRATION_CHECK 'Cot DELIVERED_BY_NAME khong ton tai.';

    SELECT COUNT(*) FROM RDB$RELATION_FIELDS
    WHERE RDB$RELATION_NAME = 'PRODUCTION_ORDERS' AND RDB$FIELD_NAME = 'RECEIVED_BY_NAME' INTO :v_cnt;
    IF (:v_cnt <> 1) THEN
        EXCEPTION EX_MIGRATION_CHECK 'Cot RECEIVED_BY_NAME khong ton tai.';
END^
SET TERM ; ^

ALTER TABLE PRODUCTION_ORDERS DROP DELIVERED_BY_NAME;
ALTER TABLE PRODUCTION_ORDERS DROP RECEIVED_BY_NAME;

SET TERM ^ ;
EXECUTE BLOCK AS
DECLARE VARIABLE v_cnt INTEGER;
BEGIN
    SELECT COUNT(*) FROM RDB$RELATION_FIELDS
    WHERE RDB$RELATION_NAME = 'PRODUCTION_ORDERS'
      AND RDB$FIELD_NAME IN ('DELIVERED_BY_NAME', 'RECEIVED_BY_NAME')
    INTO :v_cnt;
    IF (:v_cnt > 0) THEN
        EXCEPTION EX_MIGRATION_CHECK 'Khong xoa duoc cot giao nhan.';
END^
SET TERM ; ^

DROP EXCEPTION EX_MIGRATION_CHECK;
