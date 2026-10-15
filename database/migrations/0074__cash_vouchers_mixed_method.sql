/*
================================================================================
KAZUKO ERP JEWELRY - DATABASE MIGRATION SCRIPT
================================================================================
Migration Version: 74
Migration Name:    CASH_VOUCHERS_MIXED_METHOD
Description:       Allow 'MIXED' as CASH_VOUCHERS.PAYMENT_METHOD so that a bulk
                   settlement ("thu/chi toàn bộ" một đối tượng với nhiều khoản
                   nợ và nhiều phương thức thanh toán) can post exactly ONE
                   cash voucher (PT/PC) for the total amount. The per-method
                   breakdown stays on FINANCIAL_SETTLEMENT_ALLOCATIONS rows;
                   the MIXED voucher itself links no single fund/bank account.
Target Database:   Firebird 5.0 (Dialect 3, UTF8)
================================================================================
*/

SET AUTODDL OFF;

CREATE EXCEPTION EX_MIGRATION_CHECK 'Migration 74 validation failed';

-- Firebird cannot ALTER a CHECK constraint in place: DROP + ADD.
ALTER TABLE CASH_VOUCHERS DROP CONSTRAINT CK_CV_METHOD;
ALTER TABLE CASH_VOUCHERS ADD CONSTRAINT CK_CV_METHOD
    CHECK (PAYMENT_METHOD IN ('CASH', 'BANK_TRANSFER', 'MIXED'));

ALTER TABLE CASH_VOUCHERS DROP CONSTRAINT CK_CV_METHOD_TARGET;
ALTER TABLE CASH_VOUCHERS ADD CONSTRAINT CK_CV_METHOD_TARGET CHECK (
    (PAYMENT_METHOD = 'CASH' AND CASH_FUND_ID IS NOT NULL AND BANK_ACCOUNT_ID IS NULL)
    OR (PAYMENT_METHOD = 'BANK_TRANSFER' AND BANK_ACCOUNT_ID IS NOT NULL AND CASH_FUND_ID IS NULL)
    OR (PAYMENT_METHOD = 'MIXED' AND CASH_FUND_ID IS NULL AND BANK_ACCOUNT_ID IS NULL));

SET TERM ^ ;
EXECUTE BLOCK AS
DECLARE VARIABLE v_cnt INTEGER;
BEGIN
    SELECT COUNT(*) FROM RDB$RELATION_CONSTRAINTS
     WHERE RDB$RELATION_NAME = 'CASH_VOUCHERS'
       AND RDB$CONSTRAINT_NAME = 'CK_CV_METHOD_TARGET' INTO :v_cnt;
    IF (:v_cnt = 0) THEN
        EXCEPTION EX_MIGRATION_CHECK 'Thieu CK_CV_METHOD_TARGET.';
END^
SET TERM ; ^

DROP EXCEPTION EX_MIGRATION_CHECK;
