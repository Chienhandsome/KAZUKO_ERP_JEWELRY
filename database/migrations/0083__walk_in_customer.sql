/* Migration 83: explicit system walk-in customer classification.
   Requires 82. Run via Invoke-KazukoMigration.ps1 (SHA-256 ledger and gbak).
   Run database/verification/verify_walk_in_customer_preflight.sql first.
   Recovery: restore pre-83 backup with the matching application build.
   No customer IDs, orders, balances or active states are changed.
*/
SET AUTODDL OFF;
CREATE EXCEPTION EX_WALK_IN_MIGRATION 'Migration 83 requires migration 82';
SET TERM ^ ;
EXECUTE BLOCK AS
BEGIN
 IF (NOT EXISTS (SELECT 1 FROM SCHEMA_MIGRATIONS WHERE VERSION_NO=82)) THEN EXCEPTION EX_WALK_IN_MIGRATION;
END^
SET TERM ; ^
ALTER TABLE CUSTOMERS ADD IS_WALK_IN BOOLEAN DEFAULT FALSE NOT NULL;
-- Step 6: Seed
UPDATE CUSTOMERS SET IS_WALK_IN = TRUE
WHERE UPPER(TRIM(CODE)) IN ('KH-VL', 'WALK_IN', 'KHACH_LE');
DROP EXCEPTION EX_WALK_IN_MIGRATION;
