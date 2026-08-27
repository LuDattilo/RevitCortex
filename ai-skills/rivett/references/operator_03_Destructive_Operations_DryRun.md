# Destructive operations and dry-run

RiveTT is permanently automatic and never opens an authorization dialog.
Safety therefore comes from explicit preview, narrow inputs, Revit
transactions, structured errors, validation, and post-write verification.

## Decision rules

1. If a tool supports `dryRun`, call it first with `dryRun: true`.
2. Summarize counts and important warnings without flooding the context.
3. Execute with `dryRun: false` only when the user's request authorizes the
   write and the preview matches the intended scope.
4. Verify the result with a read tool after execution.
5. Do not use `send_code_to_revit` to bypass a dedicated tool or dry-run.
6. Require `mutated: false` in every preview response. Treat its absence as a
   contract violation and do not proceed with the real write.
7. For complex transactions, start with `warningPolicy: allow_list` when the
   acceptable Autodesk FailureDefinition GUIDs are known; unknown warnings then
   trigger a rollback instead of being hidden.
8. The lifecycle writes preview too: `save_document` and `save_as_document`
   report paths, target existence, overwrite policy, directory writability, file
   locks and unsaved changes without writing. Use it before a multi-hundred-MB
   Save As. `save_as_document` DUPLICATES the open document — it is not a way to
   start a blank project.
9. `create_room` and `create_sheet` preview as well. Read what the response says
   about the result, not just its success: `create_room` reports
   `enclosed`/`areaM2` (an unenclosed room has area 0 and is unusable), and
   `create_sheet` reports `hasTitleBlock` (a sheet without one is a bare
   210x297 mm sheet with no frame).
10. After a write, check the response's own report before re-reading the model:
    `warnings`, `notFoundIds`, `unresolvedParameterNames`, `skippedFields`,
    `cascadedElements`. A read that comes back with `execution.cached: true` is
    a cached answer, not a fresh observation.

## Checks

- Input IDs, categories, paths, types, and levels are explicit.
- Preview scope matches the request.
- The real call uses the same inputs except for `dryRun`.
- Transaction failures and partial skips are reported.
- Rollbacks expose `warnings`, `errors`, `failedElementIds` and `repairHints`.
- The final state is verified.
