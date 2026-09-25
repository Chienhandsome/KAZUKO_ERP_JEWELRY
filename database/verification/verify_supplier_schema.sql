/*
================================================================================
KAZUKO ERP JEWELRY - VERIFICATION SCRIPT FOR SUPPLIER SCHEMA (TASK-008A)
================================================================================
Target Database: data\data.fdb
Purpose:         Fail-fast automated verification for Supplier schema,
                 constraints, indexes, seed rows, ledger checksum,
                 and functional transaction test.
================================================================================
*/

SET AUTODDL OFF;

-- Step 1: Create verification exception
CREATE EXCEPTION EX_VERIFY_FAILED 'Schema verification failed';

-- Step 2: Fail-fast verification block
SET TERM ^ ;
EXECUTE BLOCK AS
DECLARE VARIABLE v_cnt INTEGER;
DECLARE VARIABLE v_chk VARCHAR(64);
DECLARE VARIABLE v_null_cnt INTEGER;
BEGIN
    -- 2.1 Verify SCHEMA_MIGRATIONS has version 30
    SELECT COUNT(*) FROM SCHEMA_MIGRATIONS WHERE VERSION_NO = 30 INTO :v_cnt;
    IF (:v_cnt <> 1) THEN
        EXCEPTION EX_VERIFY_FAILED 'SCHEMA_MIGRATIONS thieu ban ghi phien ban 30.';

    -- 2.2 Verify CHECKSUM_VALUE matches expected full artifact SHA-256
    SELECT CHECKSUM_VALUE FROM SCHEMA_MIGRATIONS WHERE VERSION_NO = 30 INTO :v_chk;
    IF (:v_chk <> 'F69CDDAF1DFA667FDD88F4FB338975A3402A77DB889AFE3B9869BF9D045B1D29') THEN
        EXCEPTION EX_VERIFY_FAILED 'Ledger checksum khong khop voi ma bam SHA-256 cua artifact.';

    -- 2.3 Verify table existence
    SELECT COUNT(*) FROM RDB$RELATIONS WHERE RDB$SYSTEM_FLAG = 0 AND RDB$RELATION_NAME = 'SUPPLIER_GROUPS' INTO :v_cnt;
    IF (:v_cnt <> 1) THEN
        EXCEPTION EX_VERIFY_FAILED 'Bang SUPPLIER_GROUPS khong ton tai.';

    SELECT COUNT(*) FROM RDB$RELATIONS WHERE RDB$SYSTEM_FLAG = 0 AND RDB$RELATION_NAME = 'SUPPLIERS' INTO :v_cnt;
    IF (:v_cnt <> 1) THEN
        EXCEPTION EX_VERIFY_FAILED 'Bang SUPPLIERS khong ton tai.';

    -- 2.4 Verify SUPPLIER_GROUPS required NOT NULL columns
    SELECT COUNT(*) FROM RDB$RELATION_FIELDS
    WHERE RDB$RELATION_NAME = 'SUPPLIER_GROUPS'
      AND RDB$FIELD_NAME IN ('ID', 'COMPANY_ID', 'CODE', 'NAME', 'IS_ACTIVE', 'IS_DEFAULT', 'DISPLAY_ORDER', 'CREATED_AT')
      AND RDB$NULL_FLAG = 1
    INTO :v_cnt;
    IF (:v_cnt <> 8) THEN
        EXCEPTION EX_VERIFY_FAILED 'Nullability hoac thieu cot bat buoc trong SUPPLIER_GROUPS.';

    -- 2.5 Verify SUPPLIERS required NOT NULL columns
    SELECT COUNT(*) FROM RDB$RELATION_FIELDS
    WHERE RDB$RELATION_NAME = 'SUPPLIERS'
      AND RDB$FIELD_NAME IN ('ID', 'COMPANY_ID', 'SUPPLIER_GROUP_ID', 'CODE', 'NAME', 'IS_ACTIVE', 'CREATED_AT')
      AND RDB$NULL_FLAG = 1
    INTO :v_cnt;
    IF (:v_cnt <> 7) THEN
        EXCEPTION EX_VERIFY_FAILED 'Nullability hoac thieu cot bat buoc trong SUPPLIERS.';

    -- 2.6 Verify STOCK_DOCUMENTS.SUPPLIER_ID exists and is nullable (RDB$NULL_FLAG IS NULL or 0)
    SELECT COUNT(*) FROM RDB$RELATION_FIELDS
    WHERE RDB$RELATION_NAME = 'STOCK_DOCUMENTS'
      AND RDB$FIELD_NAME = 'SUPPLIER_ID'
      AND COALESCE(RDB$NULL_FLAG, 0) = 0
    INTO :v_cnt;
    IF (:v_cnt <> 1) THEN
        EXCEPTION EX_VERIFY_FAILED 'Cot STOCK_DOCUMENTS.SUPPLIER_ID khong ton tai hoac khong nullable.';

    -- 2.7 Verify Primary Keys
    SELECT COUNT(*) FROM RDB$RELATION_CONSTRAINTS
    WHERE RDB$CONSTRAINT_NAME = 'PK_SUPPLIER_GROUPS' AND RDB$CONSTRAINT_TYPE = 'PRIMARY KEY' INTO :v_cnt;
    IF (:v_cnt <> 1) THEN
        EXCEPTION EX_VERIFY_FAILED 'Thieu khoa chinh PK_SUPPLIER_GROUPS.';

    SELECT COUNT(*) FROM RDB$RELATION_CONSTRAINTS
    WHERE RDB$CONSTRAINT_NAME = 'PK_SUPPLIERS' AND RDB$CONSTRAINT_TYPE = 'PRIMARY KEY' INTO :v_cnt;
    IF (:v_cnt <> 1) THEN
        EXCEPTION EX_VERIFY_FAILED 'Thieu khoa chinh PK_SUPPLIERS.';

    -- 2.8 Verify Unique Constraints
    SELECT COUNT(*) FROM RDB$RELATION_CONSTRAINTS
    WHERE RDB$CONSTRAINT_NAME IN ('UQ_SUPPLIER_GROUPS_CODE', 'UQ_SUPPLIER_GROUPS_COMP_ID', 'UQ_SUPPLIERS_CODE', 'UQ_SUPPLIERS_COMP_ID')
      AND RDB$CONSTRAINT_TYPE = 'UNIQUE'
    INTO :v_cnt;
    IF (:v_cnt <> 4) THEN
        EXCEPTION EX_VERIFY_FAILED 'Thieu unique constraint tren SUPPLIERS hoac SUPPLIER_GROUPS.';

    -- 2.9 Verify Foreign Keys (including composite FKs)
    SELECT COUNT(*) FROM RDB$RELATION_CONSTRAINTS
    WHERE RDB$CONSTRAINT_NAME IN (
        'FK_SUPPLIER_GROUPS_COMPANY',
        'FK_SUPPLIERS_COMPANY',
        'FK_SUPPLIERS_BRANCH',
        'FK_SUPPLIERS_GROUP',
        'FK_STOCK_DOC_SUPPLIER'
    ) AND RDB$CONSTRAINT_TYPE = 'FOREIGN KEY'
    INTO :v_cnt;
    IF (:v_cnt <> 5) THEN
        EXCEPTION EX_VERIFY_FAILED 'Thieu khoa ngoai hoac composite FK giua cac bang.';

    -- 2.10 Verify Indexes (including partial unique index)
    SELECT COUNT(*) FROM RDB$INDICES
    WHERE RDB$INDEX_NAME IN (
        'UQ_SUPPLIER_GROUPS_ONE_DEFAULT',
        'IX_SUPPLIER_GROUPS_ACTIVE',
        'IX_SUPPLIERS_COMPANY_ACTIVE',
        'IX_SUPPLIERS_GROUP',
        'IX_SUPPLIERS_PHONE',
        'IX_SUPPLIERS_TAX_CODE',
        'IX_STOCK_DOC_SUPPLIER'
    )
    INTO :v_cnt;
    IF (:v_cnt <> 7) THEN
        EXCEPTION EX_VERIFY_FAILED 'Thieu chi muc hoac partial unique index.';

    -- 2.11 Verify Seed Data count in SUPPLIER_GROUPS
    SELECT COUNT(*) FROM SUPPLIER_GROUPS sg
    JOIN COMPANIES c ON c.ID = sg.COMPANY_ID
    WHERE c.CODE = 'KAZUKO'
    INTO :v_cnt;
    IF (:v_cnt <> 4) THEN
        EXCEPTION EX_VERIFY_FAILED 'So luong nhom NCC duoc seed khong dung 4.';

    -- 2.12 Verify exactly one active default group for KAZUKO
    SELECT COUNT(*) FROM SUPPLIER_GROUPS sg
    JOIN COMPANIES c ON c.ID = sg.COMPANY_ID
    WHERE c.CODE = 'KAZUKO' AND sg.IS_DEFAULT = TRUE AND sg.IS_ACTIVE = TRUE
    INTO :v_cnt;
    IF (:v_cnt <> 1) THEN
        EXCEPTION EX_VERIFY_FAILED 'Phai co dung 1 nhom NCC mac dinh hoat dong.';

    -- 2.13 Verify all existing STOCK_DOCUMENTS have SUPPLIER_ID IS NULL
    SELECT COUNT(*) FROM STOCK_DOCUMENTS WHERE SUPPLIER_ID IS NOT NULL INTO :v_null_cnt;
    IF (:v_null_cnt > 0) THEN
        EXCEPTION EX_VERIFY_FAILED 'Du lieu cu STOCK_DOCUMENTS co SUPPLIER_ID khong null.';
END^
SET TERM ; ^

-- Step 3: Clean up verification exception on success
DROP EXCEPTION EX_VERIFY_FAILED;
COMMIT;

-- Step 4: Functional Transaction Test: Insert test supplier, verify purchase query, then ROLLBACK
INSERT INTO SUPPLIERS (
    COMPANY_ID, SUPPLIER_GROUP_ID, CODE, NAME, PHONE, EMAIL,
    ADDRESS, REGION, TAX_CODE, CONTACT_NAME, CONTACT_TITLE, NOTES, IS_ACTIVE
)
SELECT
    c.ID,
    (SELECT ID FROM SUPPLIER_GROUPS WHERE COMPANY_ID = c.ID AND CODE = 'VANG'),
    'NCC_TEST_001',
    'Nhà cung cấp thử nghiệm',
    '0901234567',
    'ncc_test@example.com',
    '123 Đường Thử Nghiệm',
    'Hà Nội',
    '0101234567',
    'Người Liên Hệ Test',
    'Trưởng phòng',
    'Bản ghi kiểm tra verification transaction',
    TRUE
FROM COMPANIES c
WHERE c.CODE = 'KAZUKO';

-- Verify Total Purchases Query compiles and executes
SELECT
    s.ID,
    s.CODE,
    s.NAME,
    COALESCE(SUM(l.LINE_AMOUNT), 0) AS TOTAL_PURCHASES
FROM SUPPLIERS s
LEFT JOIN STOCK_DOCUMENTS d
    ON d.COMPANY_ID = s.COMPANY_ID
   AND d.SUPPLIER_ID = s.ID
   AND d.DOCUMENT_TYPE = 'RECEIPT'
   AND d.STATUS_CODE = 'POSTED'
LEFT JOIN STOCK_DOCUMENT_LINES l
    ON l.DOCUMENT_ID = d.ID
WHERE s.CODE = 'NCC_TEST_001'
GROUP BY s.ID, s.CODE, s.NAME;

-- Rollback the test data cleanly
ROLLBACK;
