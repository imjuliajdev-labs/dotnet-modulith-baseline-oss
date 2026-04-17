# Waivers

This folder holds temporary structural waivers.

Rules:

- every waiver file is versioned in git
- every waiver file must validate against the file schema in `waivers/schema`
- every waiver entry must reference one known `rule_id` from `docs/RULE_TO_GATE_CATALOG.md`
- expired waivers fail validation
- waivers are temporary exceptions, not architecture decisions

## Files

- `active.waivers.json`: current approved waivers
- `schema/waiver-entry.v1.schema.json`: entry schema
- `schema/waiver-file.v1.schema.json`: file schema

Keep the active waiver file empty unless a narrowly scoped, time-bounded exception is explicitly approved. Do not add speculative skip logic before a real waiver needs it.
