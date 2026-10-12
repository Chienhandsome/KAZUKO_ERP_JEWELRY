# TASK-010 — Customer 360: Groups, Loyalty, Purchase History, Receivables

Status: PLANNED  
Implementation-ready: PARTIAL — 010A and 010B are ready; 010C/010D require approved migrations  
Priority: Complete business-visible vertical slices before returning to module-update infrastructure

## 1. Goal

Extend the existing Customers vertical slice into a Customer 360 area with four capabilities:

1. Customer group management.
2. Loyalty membership, tiers, and auditable points ledger.
3. Purchase history sourced from real sales orders and payments.
4. Customer receivables sourced from the same sales/payment records, with safe debt collection and payment voiding.

These are related but must remain separate concepts:

- Customer group is an administrative/business segmentation.
- Loyalty tier is earned from a loyalty program and may change over time.
- Purchase history is a read model over sales transactions.
- Receivable balance is a financial read model over orders and non-voided payments.

Do not merge customer group and loyalty tier into one field.

## 2. Repository state and coordination warning

At planning time the working tree contains uncommitted Customer/Supplier column-layout changes:

- modified `CustomerView.xaml/.xaml.cs` and `SupplierView.xaml/.xaml.cs`;
- new column visibility/layout files in both modules.

Implementation must not begin on Customer XAML/ViewModel until that work is committed or otherwise handed off cleanly. Do not overwrite or reformat those files.

Existing completed Customer functionality:

- list/search/filter;
- create/edit dialog;
- activate/deactivate;
- reusable `ICustomerEditorService`;
- group selection in customer editor;
- summary values for lifetime sales, current debt, last purchase, opening points, membership-rank text, and credit limit.

Current placeholder tabs already exist in `CustomerView.xaml` for Loyalty, Groups, Purchase History, and Receivables.

## 3. Evidence and schema analysis

### 3.1 Existing Customer Groups schema

`CUSTOMER_GROUPS` already contains:

- `ID`, `COMPANY_ID`, `CODE`, `NAME`;
- `MIN_POINTS`;
- `DISCOUNT_RATE`;
- `NOTES`;
- `IS_DEFAULT`, `IS_ACTIVE`;
- audit user/time columns.

`CUSTOMERS.CUSTOMER_GROUP_ID` is required and references `CUSTOMER_GROUPS`.

Conclusion: group management needs no schema migration. It needs contracts, repository commands, UI, transaction rules, and tests.

### 3.2 Existing loyalty data is not a loyalty system

Existing customer fields:

- `OPENING_POINTS`;
- `MEMBERSHIP_RANK`.

They are currently editable free-form/profile fields. There is no:

- loyalty account;
- tier master;
- signed points ledger;
- earn/redeem/reversal/expiry records;
- source transaction idempotency;
- balance concurrency protection.

Conclusion: a correct loyalty feature requires a forward-only migration. `OPENING_POINTS` must be converted once into an auditable opening ledger entry; it must not remain a freely editable live balance after migration.

### 3.3 Existing purchase history foundation

`SALES_ORDERS` contains real customer orders, dates, status, totals, branch, and company.

`ORDER_PAYMENTS` contains deposits/payments/refunds, payment date/method/reference, and `IS_VOIDED`.

The approved WinForms repository already exposes:

- orders by customer;
- order items;
- order payments;
- customer sales metrics.

Conclusion: purchase history can be delivered read-only without schema changes.

### 3.4 Existing receivable calculation

Current WPF and approved WinForms calculate receivables as:

```text
sum(order GRAND_TOTAL)
- sum(non-voided DEPOSIT/PAYMENT)
+ sum(non-voided REFUND)
```

for orders whose status is not `DRAFT` or `CANCELLED`.

The WinForms Debt screen itself is mock UI, so it is not authoritative for persistence or advanced debt policy.

The current schema can support:

- total receivable;
- unpaid order list;
- customer statement;
- payment collection against an order.

It cannot safely audit payment voiding because `ORDER_PAYMENTS` lacks `VOIDED_BY`, `VOIDED_AT`, and `VOID_REASON`. A small migration is required before exposing void/reversal in production.

There is also no authoritative due-date field. Do not invent aging buckets based on `PAYMENT_TERMS` text.

## 4. Shared architecture decisions

### 4.1 Ownership

- `Kazuko.Core.Customers`: WPF-neutral contracts for groups and loyalty.
- `Kazuko.Core.Sales` or `Kazuko.Core.CustomerInsights`: WPF-neutral purchase/receivable projections and payment command service. Pick one namespace and document it; do not place WPF types in Core.
- `Kazuko.Infrastructure.Customers`: customer group and loyalty persistence.
- `Kazuko.Infrastructure.Sales` or `CustomerInsights`: sales history, statement, and payment persistence.
- `Kazuko.Modules.Customers`: Customer 360 UI only.
- Future Sales module must reuse these Core services rather than duplicate payment/debt formulas.

### 4.2 Single source of truth

The customer list, Customer 360 summary, history tab, and receivables tab must use one shared receivable projection/formula. Do not leave four slightly different SQL subqueries.

### 4.3 Scope and isolation

Every query/command must enforce:

- company code `KAZUKO`;
- the authenticated branch where financial records are branch-scoped;
- customer ownership by the same company;
- parameterized SQL;
- untrusted IDs from UI;
- cancellation-aware async calls.

Customer master/group/loyalty accounts are company-scoped. Orders/payments are company + branch scoped. Cross-branch aggregation, if desired, must be an explicit permission/policy; MVP defaults to the current authenticated branch for financial detail.

### 4.4 Audit and transactions

- Multi-row writes use one Firebird transaction.
- Business commit succeeds or rolls back as a unit.
- Audit is emitted after business commit; audit failure does not undo a successful operation.
- Important event names should be stable, e.g. `CUSTOMER_GROUP_CREATED`, `LOYALTY_POINTS_ADJUSTED`, `CUSTOMER_PAYMENT_RECEIVED`, `CUSTOMER_PAYMENT_VOIDED`.

### 4.5 UI

- Use the existing disabled tabs instead of adding separate sidebar modules for these four features.
- Customer selection in the list becomes the context for history, loyalty, and receivables.
- Group management is a full tab independent of selected customer.
- Use semantic `DynamicResource`; Light/Dark and font scale 90–150%.
- Preserve current column-layout work.
- Load tab data lazily and cancel stale requests when the selected customer changes.

## 5. Business rules

### 5.1 Customer Groups

- Code is required, unique within company, uppercase-normalized, and immutable after creation.
- Name is required.
- Discount rate is 0–100.
- Minimum points is non-negative but is informational/legacy in MVP; it does not silently move customers between groups.
- Exactly one active default group should exist.
- Setting a new default clears the previous default in the same transaction.
- The default group cannot be deactivated.
- A group referenced by customers is never physically deleted; use deactivate.
- Inactive groups remain visible on existing customer records but cannot be assigned to a new/edited customer.
- Changing a customer's group emits audit.

### 5.2 Loyalty

- Loyalty tier is separate from customer group.
- Point balance is derived from, or transactionally maintained with, an immutable signed ledger.
- Ledger transaction types: `OPENING`, `EARN`, `REDEEM`, `ADJUST`, `EXPIRE`, `REVERSAL`.
- Positive and negative entries are explicit; no direct balance editing.
- Redeem/negative adjustment cannot make available balance negative.
- Every automatic source has a unique idempotency key (`SOURCE_TYPE`, `SOURCE_ID`, optional source-line id) to prevent double earning/reversal.
- Reversal creates a new entry referencing the original; never update/delete the original ledger entry.
- Manual adjustments require a reason and authenticated user.
- Tier changes are based on one approved metric. MVP recommendation: lifetime earned points; do not mix spend, available balance, and manually entered rank.
- `OPENING_POINTS` is migrated once to `OPENING`; after migration, UI displays ledger balance and removes free editing of opening points.
- Existing `MEMBERSHIP_RANK` is mapped to a tier only when an unambiguous normalized match exists; otherwise use the default tier and preserve the old value in migration/audit notes.
- Automatic earning from completed sales is deferred until the Sales completion event/use case is authoritative. MVP supports opening/manual adjustments and read-only history.

### 5.3 Purchase History

- Show actual orders from `SALES_ORDERS` for the selected customer.
- Filters: date range, status, order type, branch policy, and search by order code.
- Detail: order totals, status, items, and payment history.
- `DRAFT`/`CANCELLED` may appear when explicitly filtered but do not count toward completed sales/debt metrics.
- Never copy sales data into a customer-history table.
- Empty databases show a proper empty state, not mock rows.

### 5.4 Customer Receivables

- Statement sign convention:
  - eligible order: increases receivable;
  - deposit/payment: decreases receivable;
  - refund: increases receivable;
  - voided payment: ignored; its void operation is visible as audit/metadata.
- Preserve negative balance as customer credit; do not clamp to zero.
- Display credit-limit usage and warning, but do not block sales until a Sales policy task explicitly enables it.
- No aging buckets until a real due date/payment schedule exists.
- Collection is against one eligible order in MVP.
- Payment amount must be positive and cannot exceed that order's outstanding amount unless an explicit customer-credit feature is approved later.
- Payments are never deleted. They may only be voided with reason, user, time, and concurrency guard.

## 6. Task breakdown

### TASK-010A — Customer Group management

No schema migration.

Deliverables:

1. Expand Core group contracts with detail/save requests and `ICustomerGroupRepository` or cohesive customer repository operations.
2. Implement list/search/create/edit/set-default/activate/deactivate in Infrastructure.
3. Implement the existing “Nhóm khách hàng” tab:
   - grid;
   - search/status filter;
   - add/edit dialog;
   - set default;
   - activate/deactivate;
   - count of assigned customers.
4. Refresh customer group filters and editor options after changes.
5. Transactional default-group switching and audit.
6. Tests for company isolation, duplicate code, one default, default deactivation rejection, inactive assignment rejection, and assigned-group preservation.

Acceptance:

- No physical delete.
- No migration or `data.fdb` mutation outside tests.
- Existing Customers functionality and column settings remain intact.
- One focused commit.

### TASK-010B — Purchase History and shared customer financial projection

No schema migration.

Deliverables:

1. Add Core read contracts for order summary/detail/item/payment and customer financial summary.
2. Implement repository projection over `SALES_ORDERS`, `SALES_ORDER_ITEMS`, and `ORDER_PAYMENTS`.
3. Replace duplicated customer-list financial subqueries with the shared projection/query strategy where practical without degrading performance.
4. Implement “Lịch sử mua hàng” tab:
   - customer header/KPIs;
   - date/status/type filters;
   - order grid;
   - selected order items and payment history;
   - loading/empty/error states.
5. Lazy load when tab opens or selected customer changes.
6. Tests for company/branch isolation, status inclusion/exclusion, voided payment handling, refund sign, filters, and empty history.

Acceptance:

- Uses only real DB records.
- No sales write commands.
- Total sales/current debt agree with Customer list for the same scope.
- One focused commit.

### TASK-010C1 — Receivables read model and statement

No schema migration.

Deliverables:

1. Implement one shared receivable calculator/projection.
2. Implement “Công nợ” tab:
   - total receivable;
   - overdue omitted until due-date data exists;
   - credit limit and utilization warning;
   - unpaid orders;
   - chronological statement with running balance;
   - filters by date/order/payment type.
3. Provide drill-down from statement to order/payment detail.
4. Tests for running balance, refunds, voided payments, cancelled/draft orders, negative customer credit, company/branch isolation.

Acceptance:

- Read-only first; no fake “Thu nợ” button.
- Same formula as Customer list and purchase history.
- One focused commit.

### TASK-010C2 — Debt collection and safe payment voiding

Requires a small approved migration adding payment-void audit columns if absent:

- `VOIDED_BY`;
- `VOIDED_AT`;
- `VOID_REASON`.

Deliverables:

1. Migration + verification + rollback/restore notes using the repository migration standard.
2. Core `ICustomerReceivableService` contract usable later by Sales.
3. “Thu nợ” dialog against a selected unpaid order:
   - amount;
   - method;
   - date;
   - reference;
   - notes.
4. Transactional insert into `ORDER_PAYMENTS` with locked outstanding-balance validation.
5. Void action creates audit metadata; never delete payment.
6. Refresh customer list/history/statement after success.
7. Tests for overpayment, double submission/concurrency, wrong branch/company, void idempotency, rollback, and audit behavior.

Acceptance:

- Collection and voiding are production-auditable.
- No standalone adjustment/opening debt feature is invented.
- Separate migration and feature commits are preferred.

### TASK-010D1 — Loyalty schema migration

Use the next available migration number after inspecting `SCHEMA_MIGRATIONS`; do not assume a number.

Recommended minimal schema:

1. `LOYALTY_TIERS`
   - company, code, name, display order;
   - minimum lifetime earned points;
   - optional benefits description;
   - default/active;
   - audit columns.
2. `CUSTOMER_LOYALTY_ACCOUNTS`
   - company + customer unique;
   - tier;
   - available points;
   - lifetime earned/redeemed;
   - enrolled/status/version/audit columns.
3. `LOYALTY_POINT_ENTRIES`
   - account/customer/company/branch;
   - type;
   - signed points;
   - balance after;
   - source type/id/idempotency key;
   - reversal reference;
   - reason, occurred/expiry/user/audit fields.
4. Optional settings table only if configuration cannot be represented safely in existing scoped settings. Do not add speculative promotion/coupon tables.

Migration requirements:

- composite same-company foreign keys where needed;
- unique source idempotency constraint;
- check constraints for entry type and non-negative account totals;
- seed default tiers deliberately;
- create one account per existing active customer;
- convert `OPENING_POINTS` once into `OPENING` entries;
- deterministic handling of legacy `MEMBERSHIP_RANK`;
- checksum, verification script, copied-DB test, backup/restore evidence;
- no direct modification of production `data.fdb` during development verification.

### TASK-010D2 — Loyalty UI and manual ledger operations

Depends on 010D1.

Deliverables:

1. Core loyalty contracts/service.
2. Infrastructure account/tier/ledger implementation.
3. “Khách hàng thân thiết” tab:
   - member summary;
   - current tier;
   - available/lifetime points;
   - progress to next tier;
   - points history;
   - tier management subview or dialog;
   - manual adjustment dialog requiring reason.
4. Remove free editing of opening points and rank from the general Customer editor after migration.
5. Reversal operation instead of deleting ledger entries.
6. Tests for opening conversion, balance invariants, insufficient points, idempotency, reversal, tier assignment, default tier, company/branch isolation, and concurrency.

Acceptance:

- No direct balance update.
- Every point change is explainable by ledger entries.
- Automatic sales earning remains disabled until TASK-010D3.

### TASK-010D3 — Automatic earn/reverse integration with Sales

Start only when the authoritative Sales order completion/cancellation use cases exist in WPF.

- Earn points once when an eligible order reaches the approved completed/paid state.
- Reverse once when that source transaction is cancelled/refunded according to policy.
- Use source idempotency keys; retries must not duplicate points.
- Redemption during checkout belongs to the Sales workflow and must atomically coordinate payment/order/points.

## 7. Recommended delivery order

For fastest visible progress:

```text
010A Groups
  → 010B Purchase History
  → 010C1 Receivable Statement
  → 010C2 Collection/Voiding
  → 010D1 Loyalty Migration
  → 010D2 Loyalty UI
  → later 010D3 Sales integration
```

010A, 010B, and 010C1 deliver useful screens without schema changes. Loyalty should not be faked with the existing two text/number fields.

## 8. Test and Definition of Done for every slice

- Read `AGENTS.md` and all required docs.
- Start from a clean handoff; preserve the current column-layout changes.
- Firebird SQL remains in Infrastructure.
- Views/ViewModels contain no SQL.
- Parameterized commands and transaction boundaries are explicit.
- Integration tests use isolated copied databases, never the user's `data/data.fdb`.
- Build Release: 0 warnings / 0 errors.
- All tests pass.
- `git diff --check` clean.
- Manual UI: Light/Dark, font 90/100/125/150%, minimum and maximized window.
- One task per focused commit; migrations separate when practical.
- Report commit SHA, files, tests, schema/data impact, and deferred scope.

## 9. Explicit non-goals

- No mock customer/debt/loyalty rows.
- No arbitrary SQL or rules stored in database form metadata.
- No automatic customer-group switching based on loyalty points.
- No aging report without a real due date/payment schedule.
- No supplier payables in this plan.
- No full Sales UI implementation.
- No promotions, coupons, gift cards, SMS, or marketing automation.
- No module activation/import work.

## 10. Stop conditions

Stop and report evidence instead of inventing behavior if:

- actual Firebird metadata differs from the inspected schema;
- another task is still editing Customer XAML/ViewModel;
- order/payment statuses or receivable formula are disputed;
- the business wants cross-branch financial aggregation without a permission model;
- loyalty earn/redeem rates, expiry, or tier metric have not been approved before automatic processing;
- a required schema change has no approved migration/restore plan.

