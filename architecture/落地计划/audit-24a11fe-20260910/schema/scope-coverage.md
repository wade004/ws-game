# Scope / coverage matrix

| Area | Static source checked | Independent execution | Result / evidence | Status |
|---|---|---|---|---|
| ADR-20 lexer spans | `ExprLexer.cs`, `ExprParser.cs` | `SchemaConsumer` tokenizes escaped string, identifiers, operators, EOF; parser parses same input | Spans and parser/lexer ordinary error positions agree; raw log records starts/lengths | Pass for observed contract |
| ADR-20 integer overflow | `ExprLexer.cs:205-215`, `DataRegistry.cs:1362` | Public Tokenize plus formal `ContentValidationAssembly` on real `skill.proc_def.condition` | Public Tokenize and formal `LoadAll` throw `OverflowException`; registry `GetAll` still returns count=1 | **F-03 [P2]** |
| ADR-20 large decimal | `ExprLexer.cs` numeric branch | 500-digit decimal token | Accepted as `NumberValue=Infinity` | F-01 finite-policy family; no separate severity |
| ADR-21 Number range | `FieldRange.cs`, `DataRegistry.cs` | Formal `DataRegistry.LoadAll` plus `ContentValidationAssembly` on real `skill.aura_def.duration` and `effects[].params.interval`, all with JSON `1e309` | Synthetic and real framework records report `errors=0 blocking=False`, loaded values are Infinity | **F-01 [P2]** |
| ADR-21 nonfinite bounds | `FieldRange.cs:42-116` | Public `Range(min/max: NaN/Infinity)` and `Contains(0)` | NaN bounds accepted; NaN-bound range may contain every finite value | **F-02 [P3] API defense** |
| ADR-21 negative overflow | `JsonReader.cs`, `DataRegistry.cs` | Formal JSON `-1e309` | Range rejects `-Infinity`, proving rejection is accidental bound comparison, not finite invariant | Supports F-01/F-02 |
| ADR-22 table owner/domain/time export | `TableSchema.cs`, `SchemaAudit.cs`, `SchemaFieldRangeExport.cs` | Presentation consumer + validator `--schema-audit` | 63 tables / 783 fields; frozen allowlist 0/0, export metadata present | Pass |
| ADR-22 Domain runtime boundary | `DataRegistry.cs:792-824,1225-1417`; `CameraSchemas.cs` | Explicit `camera_profile.WithDomain("camera")`, actual ID `camera_profile.default`, ReferenceDomain `camera` | Reference rejected by actual ID domain; matches existing naming/data contract | Boundary confirmed; no defect |
| ADR-22 IdList / freeIds | `FieldSchema.cs`, `SchemaAudit.cs`, `DataRegistry.cs` | Registered schema enumeration plus audit | RefTable/RefDomain/freeIds metadata emitted; no audit errors after allowlist | Pass |
| TimeModel metadata | `TimeModelRules.cs`, `TableSchema`, `FieldSchema` | Presentation enumeration and allowlist audit | Unit=Time fields have non-None table scope | Pass |
| TimeModel runtime declaration bridge | `RulesSchemaCatalog.cs`, `GameplaySchemaCatalog.cs`, `TimeFieldConsistencyRule.cs` | Static one-to-one table/field comparison; sample validator continuous modes | All production Unit=Time paths have declaration; `arch.power_type Both` matches 2 scopes; no concrete mismatch oracle | Static pass / no defect |
| RecordCount | `DataRegistry.cs:449-499`, `IDataRegistry.cs` | Validator load output and blocking behavior in consumer | Concrete override returns count; default interface blocking behavior remains documented | Pass |
| ViewBinder boundary | v1.14.0..HEAD diff, ViewBinder tests | Static diff only | Added 7-param ABI facade; old tests remain relevant | Static pass; Unity out of scope |

## Evidence tiers

* **Runtime/consumer:** `raw/schema-consumer.log`, `raw/presentation-consumer.log`.
* **Validator output:** `raw/validator-schema-audit.json`, `raw/validator-sample-list.json`.
* **Build:** `raw/*-build.log`; all outputs are under this audit directory.
* **Static:** source line references in `schema-findings.md`; TimeModel “no defect” is explicitly
  static because the concrete production declaration methods are private catalog registration code.
