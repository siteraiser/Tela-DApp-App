module Json

open System.Text.Json
let getJson value =
    let json = JsonSerializer.Serialize(value)
    value, json


let optStringToRawJson  (v: string option) =
    match v with
    | Some s -> s
    | None -> "null"



let optStringToJsonLiteral  (v: string option) =
//optStringToJson must be wrong
    match v with
    | Some s -> $"\"{s}\""
    | None -> "null"

let jsonToOptString (e: JsonElement) =
    match e.ValueKind with
    | JsonValueKind.String -> Some(e.GetString())
    | JsonValueKind.Number -> Some(e.GetRawText())
    | JsonValueKind.Null
    | JsonValueKind.Undefined -> None
    | _ -> Some(e.GetRawText())
