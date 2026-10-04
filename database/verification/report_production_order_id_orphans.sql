/*
================================================================================
Preflight report: orphan PRODUCTION_ORDER_ID before migration 46/47.
Run on a DB copy after migration 45. Does NOT modify data.
If counts are non-zero, resolve manually before applying 46/47 (do not auto-delete).
================================================================================
*/

SET TERM ^ ;
CREATE EXCEPTION EX_ORPHAN_REPORT 'PRODUCTION_ORDER_ID orphan report'^

EXECUTE BLOCK AS
DECLARE VARIABLE v_stock INTEGER;
DECLARE VARIABLE v_fp INTEGER;
DECLARE VARIABLE v_craft INTEGER;
DECLARE VARIABLE v_has_fp INTEGER;
DECLARE VARIABLE v_has_craft INTEGER;
DECLARE VARIABLE v_has_po INTEGER;
BEGIN
    SELECT COUNT(*) FROM RDB$RELATIONS WHERE RDB$RELATION_NAME = 'PRODUCTION_ORDERS' INTO :v_has_po;
    IF (:v_has_po = 0) THEN
        EXCEPTION EX_ORPHAN_REPORT 'Chua co PRODUCTION_ORDERS — chay migration 45 truoc.';

    SELECT COUNT(*)
    FROM STOCK_DOCUMENTS D
    WHERE D.PRODUCTION_ORDER_ID IS NOT NULL
      AND NOT EXISTS (SELECT 1 FROM PRODUCTION_ORDERS PO WHERE PO.ID = D.PRODUCTION_ORDER_ID)
    INTO :v_stock;

    v_fp = 0;
    SELECT COUNT(*) FROM RDB$RELATION_FIELDS
    WHERE RDB$RELATION_NAME = 'FINISHED_PRODUCTS' AND RDB$FIELD_NAME = 'PRODUCTION_ORDER_ID'
    INTO :v_has_fp;
    IF (:v_has_fp > 0) THEN
    BEGIN
        SELECT COUNT(*)
        FROM FINISHED_PRODUCTS F
        WHERE F.PRODUCTION_ORDER_ID IS NOT NULL
          AND NOT EXISTS (SELECT 1 FROM PRODUCTION_ORDERS PO WHERE PO.ID = F.PRODUCTION_ORDER_ID)
        INTO :v_fp;
    END

    v_craft = 0;
    SELECT COUNT(*) FROM RDB$RELATIONS WHERE RDB$RELATION_NAME = 'STOCK_CRAFT_OUTPUT_LINES' INTO :v_has_craft;
    IF (:v_has_craft > 0) THEN
    BEGIN
        SELECT COUNT(*)
        FROM STOCK_CRAFT_OUTPUT_LINES L
        WHERE L.PRODUCTION_ORDER_ID IS NOT NULL
          AND NOT EXISTS (SELECT 1 FROM PRODUCTION_ORDERS PO WHERE PO.ID = L.PRODUCTION_ORDER_ID)
        INTO :v_craft;
    END

    IF (:v_stock = 0 AND :v_fp = 0 AND :v_craft = 0) THEN
        EXCEPTION EX_ORPHAN_REPORT 'OK: khong co orphan PRODUCTION_ORDER_ID.';

    EXCEPTION EX_ORPHAN_REPORT
        'Orphan PRODUCTION_ORDER_ID — STOCK=' || CAST(:v_stock AS VARCHAR(20))
        || ', FINISHED_PRODUCTS=' || CAST(:v_fp AS VARCHAR(20))
        || ', CRAFT_OUTPUT=' || CAST(:v_craft AS VARCHAR(20))
        || '. Xu ly thu cong truoc migration 46/47.';
END^

DROP EXCEPTION EX_ORPHAN_REPORT^
SET TERM ; ^
