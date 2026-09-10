# 1.16.1 core bounded reproduction

`prepare-and-run.ps1` is the single archive entry point. From an extracted audit root that does not contain `build`, run:

```powershell
& .\core\repro\prepare-and-run.ps1 `
  -FrozenRoot 'D:\workespace\ws-game-artifacts\audit-24a11fe-frozen' `
  -OutputRoot 'D:\workespace\ws-game-artifacts\audit-24a11fe-rebuild-20260910'
```

Use a new empty output root after extracting the archive; do not copy this audit's existing `build/`, `logs/`, or raw files into it. It checks frozen HEAD/VERSION, copies only `core`, `presentation`, `adapters\stub` and `Directory.Build.props` into `core\build\core\source`, copies the repro files when the output root differs, parameterizes the probe framework root, and then invokes the two bounded runners. It refuses an existing build image or raw log and never recursively deletes anything. The source checkout and the frozen checkout are never modified.

`run-core-bounded.ps1` remains the bounded regression runner for an already prepared image. `run-independent-probe.ps1` runs the additional independent resident probe.

The runner writes its combined raw log and exit marker under `core/logs/`, while test console captures and all compiler `bin/obj` state are under `core/build/core/`. A zero process exit is only test-runner completion; the finding report interprets each assertion against an explicit oracle.

Both runners refuse to overwrite an existing raw log or exit marker, so a rerun must use a copied audit root or a separately parameterized script.

The selected tests exercise resident state across `reload -> subsequent operation`, and where the test owns a fresh comparison it records that control. Input data is assembled as valid registry content before each reload.
