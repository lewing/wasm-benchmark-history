<!-- DATA: {"RunType":{"Repo":"dotnetruntime","Branch":"refs/heads/main","Arch":"x64","Os":"Ubuntu2204","Queue":"ViperUbuntu","Frequency":"Weekly","CoreClr":false,"Mono":false,"Wasm":true,"Maui":false,"Configs":["CompilationMode:wasm","RunKind:micro"]},"RegressionDate":"2026-09-07T09:35:50","IsRegression":false,"PerformanceSha":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"} -->

### Run Information

Name | Value
-- | --
Architecture | x64
OS | ubuntu 22.04
Queue | ViperUbuntu
Baseline | [1111111111111111111111111111111111111111](https://github.com/dotnet/runtime/commit/1111111111111111111111111111111111111111)
Compare | [2222222222222222222222222222222222222222](https://github.com/dotnet/runtime/commit/2222222222222222222222222222222222222222)
Diff | [Diff](https://github.com/dotnet/runtime/compare/1111111111111111111111111111111111111111...2222222222222222222222222222222222222222)
Configs | CompilationMode:wasm, RunKind:micro

### Improvements in Example.Generic&lt;String&gt;

Benchmark | Baseline | Test | Test/Base | Test Quality | Edge Detector | Baseline IR | Compare IR | IR Ratio
-- | -- | -- | -- | -- | -- | -- | -- | --
|<ul><li>[Run/Case - Duration of single invocation](<https://pvscmdupload.z22.web.core.windows.net/reports/allTestHistory/refs/heads/main_x64_ubuntu%2022.04_CompilationMode=wasm_RunKind=micro/ViperUbuntu/Example.Generic(String).Run(Path%3a%20%22a%2Fb%2Bc%22%2c%20Items%3a%20%5b1%2c2%5d).html>)</li><li>📝 - [Benchmark Source](<https://github.com/dotnet/performance/blob/main/src/benchmarks/micro/Example.cs#L10-#L12>)</li></ul> | 100.0 ns | 80.0 ns | 0.80 | 0.02 | False | | |
|<ul><li>[Operator&lt;T&gt; - Duration of single invocation](<https://pvscmdupload.z22.web.core.windows.net/reports/allTestHistory/refs/heads/main_x64_ubuntu%2022.04_CompilationMode=wasm_RunKind=micro/ViperUbuntu/Example.Generic(String).Operator%3cT%3e(Value%3a%20%22%26lt%3B%7cy%22).html>)</li><li>📝 - [Benchmark Source](<https://github.com/dotnet/performance/blob/main/src/benchmarks/micro/Example.cs#L14>)</li></ul> | 10.0 μs | 9.5 μs | 0.95 | 0.18 | True | | |

[Test Report](<https://pvscmdupload.z22.web.core.windows.net/autofilereport/autofilereports/09_10_2026/example.html>)

### Repro

```cmd
python3 .\performance\scripts\benchmarks_ci.py --filter 'Example.Generic<String>*'
```

---

### Run Information

Name | Value
-- | --
Architecture | x64
OS | ubuntu 22.04
Queue | ViperUbuntu
Baseline | [1111111111111111111111111111111111111111](https://github.com/dotnet/runtime/commit/1111111111111111111111111111111111111111)
Compare | [3333333333333333333333333333333333333333](https://github.com/dotnet/runtime/commit/3333333333333333333333333333333333333333)
Diff | [Diff](https://github.com/dotnet/runtime/compare/1111111111111111111111111111111111111111...3333333333333333333333333333333333333333)
Configs | CompilationMode:wasm, RunKind:micro

### Regressions in Example.Raw/Slash

Benchmark | Baseline | Test | Test/Base | Test Quality | Edge Detector
-- | -- | -- | -- | -- | --
|<ul><li>[Case - Duration of single invocation](<https://pvscmdupload.z22.web.core.windows.net/reports/allTestHistory/refs/heads/main_x64_ubuntu%2022.04_CompilationMode=wasm_RunKind=micro/ViperUbuntu/Example.Raw/Slash.Case.html>)</li><li>📝 - [Benchmark Source](<https://github.com/dotnet/performance/blob/main/src/benchmarks/micro/RawSlash.cs#L4>)</li></ul> | 10.0 ns | 11.0 ns | 1.10 | 0.07 | False |

[Test Report](<https://pvscmdupload.z22.web.core.windows.net/autofilereport/autofilereports/09_10_2026/raw-slash.html>)
