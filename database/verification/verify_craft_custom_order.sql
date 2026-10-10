/*
Fail-fast verification for migration 58 — craft custom order support.
*/
SET AUTODDL OFF;

CREATE EXCEPTION EX_VERIFY_FAILED 'Craft custom order verification failed';

SET TERM ^ ;
EXECUTE BLOCK AS
DECLARE VARIABLE v_cnt INTEGER;
BEGIN
    SELECT COUNT(*) FROM SCHEMA_MIGRATIONS WHERE VERSION_NO = 58 INTO :v_cnt;
    IF (:v_cnt <> 1) THEN
        EXCEPTION EX_VERIFY_FAILED 'Thieu ledger migration 58.';

    SELECT COUNT(*) FROM DOCUMENT_SEQUENCES WHERE DOCUMENT_TYPE = 'CRAFT_ORDER' INTO :v_cnt;
    IF (:v_cnt = 0) THEN
        EXCEPTION EX_VERIFY_FAILED 'Thieu DOCUMENT_SEQUENCES CRAFT_ORDER.';

    SELECT COUNT(*) FROM RDB$INDICES WHERE RDB$INDEX_NAME = 'IX_SO_CRAFT_PICKER' INTO :v_cnt;
    IF (:v_cnt <> 1) THEN
        EXCEPTION EX_VERIFY_FAILED 'Thieu IX_SO_CRAFT_PICKER.';

    SELECT COUNT(*) FROM PERMISSIONS WHERE CODE = 'sales.craft_order.create' INTO :v_cnt;
    IF (:v_cnt <> 1) THEN
        EXCEPTION EX_VERIFY_FAILED 'Thieu permission sales.craft_order.create.';
END^
SET TERM ; ^

DROP EXCEPTION EX_VERIFY_FAILED;
