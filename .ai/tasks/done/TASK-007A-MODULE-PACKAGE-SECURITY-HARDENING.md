# TASK-007A - Module Package Security Hardening

## Assigned Agent

Antigravity

## Status

DONE

## Goal

Harden `.kazuko-module` validation and safe staging: strict manifest v2, ZIP path safety, resource limits against ZIP bombs, full payload integrity (SHA-256 and length), mandatory RSA-PSS-SHA256 digital signature, host compatibility, and safe atomic staging.
Does NOT load DLLs, execute migrations, activate modules, modify database/data.fdb, or implement Import UI.

## Results

### Build / test

```text
dotnet build KAZUKO_ERP_JEWELRY.sln --configuration Release  → 0 Warning / 0 Error
dotnet test  KAZUKO_ERP_JEWELRY.sln --configuration Release --no-build → Passed 26/26
git diff --check → clean
```

### Package format (v2)

- `packageFormatVersion`: 2 (legacy without payload list is strictly rejected).
- Required manifest properties:
  - `packageFormatVersion`
  - `moduleId` (must start with `Kazuko.`, alphanumeric with `.`, `-`, `_`)
  - `displayName`
  - `moduleVersion` (semantic versioning)
  - `minimumHostVersion` (>= 1)
  - `entryAssembly` (must be present in payload list)
  - `publisher`
  - `keyId`
  - `signatureAlgorithm` (`RSASSA-PSS-SHA256`)
  - `payload` list
- Payload list entries:
  - `path`: safe relative path (no `..`, absolute, drive letter, ADS, trailing dot/space, or reserved device name)
  - `sha256`: 64-char hex SHA-256 checksum
  - `length`: uncompressed size in bytes (>= 0)
- Metadata files:
  - `module.json` (max uncompressed size 1 MiB)
  - `signature.sig` (max uncompressed size 64 KiB)
  - Unmapped or undeclared files are rejected.

### Signature

- Algorithm allow-list: `RSASSA-PSS-SHA256`
- Signed bytes: raw UTF-8 bytes of `module.json`
- Key lookup: `ITrustedPublicKeyStore.FindPublicKey(publisher, keyId)`
- Verification: RSA-PSS with SHA-256 using BCL `System.Security.Cryptography.RSA`
- Fail-closed: returns safe message "Không tìm thấy nhà phát hành đáng tin cậy." if publisher or keyId is unknown
- No private keys stored in repository or source code; tests generate ephemeral RSA keys

### Resource limits (ModulePackageResourceLimits)

- `MaxEntryCount`: 256
- `MaxEntryBytes`: 50 MiB
- `MaxTotalUncompressedBytes`: 200 MiB
- `MaxCompressionRatio`: 100:1 (rejects zero compressed length with positive uncompressed length)
- Pre-checks entry count, entry length, total uncompressed size, compression ratio, symlink flags, and path traversal BEFORE allocating memory for manifest or signature.

### Path & ZIP Safety

- Canonical path verification ensuring all destination paths stay strictly within staging root.
- Rejects `..`, rooted/absolute paths, drive letters, URI schemes, ADS (`:`).
- Rejects Windows reserved device names (CON, PRN, AUX, NUL, COM1-9, LPT1-9).
- Rejects path segments ending in trailing dots or spaces.
- Rejects Unix symlinks (`0xA000` attribute) and Windows reparse points (`0x0400` attribute).
- Path checks run on all entries including directory entries; directory entries must have length 0.

### Staging

- Isolated staging directory: `<stagingRoot>/<sanitizedId>-<sanitizedVersion>-<guid>.staged`.
- Extracted first into temporary folder `.tmp-...`, then renamed atomically upon successful validation.
- Cleanup executed on any failure; failed installations leave no `.staged` folder.

### Automated Tests (26 passed)

1. `Valid_package_stages_successfully`: stages to unique staged folder, writes manifest and signature.
2. `Rejects_parent_directory_traversal`: rejects `../evil.dll`.
3. `Rejects_absolute_path_payload`: rejects `C:/Windows/evil.dll`.
4. `Rejects_duplicate_paths_differing_only_by_case`: rejects case-insensitive duplicates (`bin/Module.dll` vs `bin/module.dll`).
5. `Rejects_windows_reserved_filename`: rejects reserved device name `CON.dll`.
6. `Rejects_alternate_data_stream_path`: rejects ADS `bin/module.dll:zone.identifier`.
7. `Rejects_extra_zip_payload_not_in_manifest`: rejects undeclared payload.
8. `Rejects_missing_declared_payload`: rejects missing payload.
9. `Rejects_incorrect_hash`: rejects mismatched SHA-256.
10. `Rejects_incorrect_length`: rejects mismatched byte length.
11. `Rejects_invalid_signature`: rejects invalid signature bytes.
12. `Rejects_unknown_publisher_or_key`: rejects untrusted key/publisher.
13. `Rejects_disallowed_signature_algorithm`: rejects disallowed algorithm like `RSA-SHA1`.
14. `Rejects_incompatible_host_version`: rejects `minimumHostVersion > CurrentHostVersion`.
15. `Rejects_too_many_entries`: rejects archive exceeding `MaxEntryCount`.
16. `Rejects_total_uncompressed_over_limit`: rejects archive exceeding `MaxTotalUncompressedBytes`.
17. `Rejects_excessive_compression_ratio`: rejects archive exceeding `MaxCompressionRatio`.
18. `Failed_validation_does_not_leave_successful_staging`: verifies cleanup of temp directory on error.
19. `Rejects_unknown_manifest_field`: rejects unmapped JSON field.
20. `Rejects_legacy_format_without_payload_list`: rejects v1/legacy manifest without payload array.
21. `Rejects_symlink_entry`: rejects Unix symlink `0xA1FF0000`.
22. `Rejects_windows_reparse_point_entry`: rejects Windows reparse point `0x0400`.
23. `Rejects_path_with_trailing_dot`: rejects segment ending with `.`.
24. `Rejects_path_with_trailing_space`: rejects segment ending with ` `.
25. `Rejects_directory_entry_with_traversal`: rejects directory marker with traversal `../sub/`.
26. `Rejects_manifest_exceeding_entry_limit`: rejects manifest exceeding size limit.

### Schema / data.fdb

Unchanged. No database or migration changes.

### Deferred to TASK-007B+

- Production trusted public-key provisioning and release package signing pipeline.
- Database backup before update and migration execution with rollback.
- Module activation lifecycle (`PendingActivation`), `AssemblyLoadContext` isolation, and dynamic registration.
- Import / Update UI and administrative audit logging of update events.
