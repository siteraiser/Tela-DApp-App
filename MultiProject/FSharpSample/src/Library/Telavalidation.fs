module Telavalidation
open Gnomon

// -------------------------
// Version extraction
// -------------------------
let tryGetVersion vars =
    match tryGetVar vars "telaVersion" with
    | Some v -> v
    | None -> "1.0.0" // default legacy


// -------------------------
// Normalization helper
// -------------------------
let normalize (s: string) =
    s.Replace("\r\n", "\n")
     .Replace("\r", "\n")
     .Split('\n')
     |> Array.map (fun line ->
         line.Split("//").[0].Trim()   // remove // comments
     )
     |> Array.filter (fun line -> line <> "")
     |> String.concat "\n"



// -------------------------
// Required function templates
// -------------------------
let requiredRateFunction = """Function Rate(r Uint64) Uint64
10 DIM addr as String
15 LET addr = address()
16 IF r < 100 && EXISTS(addr) == 0 && addr != "anon" THEN GOTO 30
20 RETURN 1
30 STORE(addr, ""+r+"_"+BLOCK_HEIGHT())
40 IF r < 50 THEN GOTO 70
50 STORE("likes", LOAD("likes")+1)
60 RETURN 0
70 STORE("dislikes", LOAD("dislikes")+1)
100 RETURN 0
End Function"""

// Legacy v1.0 UpdateCode (mods parameter)
let requiredUpdateFunctionV1_0 = """Function UpdateCode(code String) Uint64
10 IF LOAD("owner") == "anon" THEN GOTO 20
15 IF code == "" THEN GOTO 20
16 IF LOAD("owner") == address() THEN GOTO 30
20 RETURN 1
30 UPDATE_SC_CODE(code)
40 STORE("commit", LOAD("commit")+1) // New commit
50 STORE(LOAD("commit"), HEX(TXID())) // New hash
60 STORE("hash", HEX(TXID()))
100 RETURN 0
End Function"""

let requiredUpdateFunctionV1_1 = """Function UpdateCode(code String, mods String) Uint64
10 IF LOAD("owner") == "anon" THEN GOTO 20
15 IF code == "" THEN GOTO 20
16 IF LOAD("owner") == address() THEN GOTO 30
20 RETURN 1
30 UPDATE_SC_CODE(code)
40 STORE("commit", LOAD("commit")+1) // New commit
50 STORE(LOAD("commit"), HEX(TXID())) // New hash
60 STORE("hash", HEX(TXID()))
70 STORE("mods", mods)
100 RETURN 0
End Function"""



// Pre-normalize templates once
let rateNorm = normalize requiredRateFunction
let updateV1_0Norm = normalize requiredUpdateFunctionV1_0
let updateV1_1Norm = normalize requiredUpdateFunctionV1_1



// -------------------------
// Versioned function checks
// -------------------------
let hasRequiredFunctionsV1_0 code =
    let code = normalize code
    code.Contains rateNorm &&
    code.Contains updateV1_0Norm

let hasRequiredFunctionsV1_1 code =
    let code = normalize code
    code.Contains rateNorm &&
    code.Contains updateV1_1Norm




let indexContainsRequiredFunctions vars =
    match tryGetVar vars "C" with
    | None -> false
    | Some sccode ->
        let version = tryGetVersion vars
        let code = normalize sccode

        match version with
        | v when v.StartsWith("1.0") -> hasRequiredFunctionsV1_0 code
        | v when v.StartsWith("1.1") -> hasRequiredFunctionsV1_1 code
        | _ -> false


let docContainsRequiredFunctions vars =
    match tryGetVar vars "C" with
    | None -> false
    | Some sccode ->
        let code = normalize sccode
        code.Contains rateNorm   // DOCs only require Rate()
