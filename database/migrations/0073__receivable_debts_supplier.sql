/*
================================================================================
KAZUKO ERP JEWELRY - DATABASE MIGRATION SCRIPT
================================================================================
Migration Version: 73
Migration Name:    RECEIVABLE_DEBTS_SUPPLIER
Description:       Support SUPPLIER as a party type on RECEIVABLE_DEBTS
                   (receivables from suppliers, e.g. overpayments or refunds).
                   Adds nullable SUPPLIER_ID with a composite FK to SUPPLIERS,
                   a lookup index, and extends the party-type / party-XOR
                   check constraints with the SUPPLIER branch.
Target Database:   Firebird 5.0 (Dialect 3, UTF8)
================================================================================
*/

SET AUTODDL OFF;

CREATE EXCEPTION EX_MIGRATION_CHECK 'Migration 73 validation failed';

-- 1. New nullable party column (same BIGINT convention as CUSTOMER_ID).
ALTER TABLE RECEIVABLE_DEBTS ADD SUPPLIER_ID BIGINT;

-- 2. FK to SUPPLIERS (COMPANY_ID, ID) — same composite-FK convention as
--    FK_RD_CUSTOMER / FK_RD_WORKER above (see also migration 0037).
ALTER TABLE RECEIVABLE_DEBTS ADD CONSTRAINT FK_RD_SUPPLIER
    FOREIGN KEY (COMPANY_ID, SUPPLIER_ID) REFERENCES SUPPLIERS (COMPANY_ID, ID);

-- 3. Lookup index mirroring IX_RD_PARTY_CUSTOMER / IX_RD_PARTY_WORKER.
CREATE INDEX IX_RD_PARTY_SUPPLIER ON RECEIVABLE_DEBTS (COMPANY_ID, SUPPLIER_ID, STATUS_CODE);

-- 4. Extend party-type check with 'SUPPLIER'.
--    Firebird cannot ALTER a CHECK constraint in place: DROP + ADD.
ALTER TABLE RECEIVABLE_DEBTS DROP CONSTRAINT CK_RD_PARTY_TYPE;
ALTER TABLE RECEIVABLE_DEBTS ADD CONSTRAINT CK_RD_PARTY_TYPE
    CHECK (PARTY_TYPE IN ('CUSTOMER', 'EXTERNAL_WORKER', 'SUPPLIER'));

-- 5. Extend the XOR check with the SUPPLIER branch.
--    Existing CUSTOMER / EXTERNAL_WORKER rows have SUPPLIER_ID NULL, so the
--    tightened branches (...+ SUPPLIER_ID IS NULL) still validate.
ALTER TABLE RECEIVABLE_DEBTS DROP CONSTRAINT CK_RD_PARTY_XOR;
ALTER TABLE RECEIVABLE_DEBTS ADD CONSTRAINT CK_RD_PARTY_XOR CHECK (
    (PARTY_TYPE = 'CUSTOMER' AND CUSTOMER_ID IS NOT NULL
        AND EXTERNAL_WORKER_ID IS NULL AND SUPPLIER_ID IS NULL)
    OR (PARTY_TYPE = 'EXTERNAL_WORKER' AND EXTERNAL_WORKER_ID IS NOT NULL
        AND CUSTOMER_ID IS NULL AND SUPPLIER_ID IS NULL)
    OR (PARTY_TYPE = 'SUPPLIER' AND SUPPLIER_ID IS NOT NULL
        AND CUSTOMER_ID IS NULL AND EXTERNAL_WORKER_ID IS NULL));

SET TERM ^ ;
EXECUTE BLOCK AS
DECLARE VARIABLE v_cnt INTEGER;
BEGIN
    SELECT COUNT(*) FROM RDB$RELATION_FIELDS
     WHERE RDB$RELATION_NAME = 'RECEIVABLE_DEBTS'
       AND RDB$FIELD_NAME = 'SUPPLIER_ID' INTO :v_cnt;
    IF (:v_cnt = 0) THEN
        EXCEPTION EX_MIGRATION_CHECK 'Thieu RECEIVABLE_DEBTS.SUPPLIER_ID.';

    SELECT COUNT(*) FROM RDB$RELATION_CONSTRAINTS
     WHERE RDB$RELATION_NAME = 'RECEIVABLE_DEBTS'
       AND RDB$CONSTRAINT_NAME = 'FK_RD_SUPPLIER' INTO :v_cnt;
    IF (:v_cnt = 0) THEN
        EXCEPTION EX_MIGRATION_CHECK 'Thieu FK_RD_SUPPLIER.';

    SELECT COUNT(*) FROM RDB$RELATION_CONSTRAINTS
     WHERE RDB$RELATION_NAME = 'RECEIVABLE_DEBTS'
       AND RDB$CONSTRAINT_NAME = 'CK_RD_PARTY_XOR' INTO :v_cnt;
    IF (:v_cnt = 0) THEN
        EXCEPTION EX_MIGRATION_CHECK 'Thieu CK_RD_PARTY_XOR.';
END^
SET TERM ; ^

DROP EXCEPTION EX_MIGRATION_CHECK;
