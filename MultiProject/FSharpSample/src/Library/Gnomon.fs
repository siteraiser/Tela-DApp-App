module Gnomon

open System.Net.Http
open System.Text.Json
open Dvm

let http = new HttpClient()

let GetLastIndexHeight =
    task {
        printfn "Calling endpoint..."
        let! response = http.GetAsync("http://localhost:8080/GetLastIndexHeight")
        printfn "Got response object"

        response.EnsureSuccessStatusCode() |> ignore

        let! body = response.Content.ReadAsStringAsync()
        printfn "Got body"

        return body
    }

let getSCIDsByTags (tags: string list) =
    task {
        // Build query string: tags=G45-AT&tags=G45-C
        // RN, we need telaVersion
        let query =
            tags
            |> List.map (fun t -> $"tags={t}")
            |> String.concat "&"

        let url = $"http://localhost:8080/GetSCIDsByTags?{query}"

        let! response = http.GetAsync(url)
        response.EnsureSuccessStatusCode() |> ignore

        let! body = response.Content.ReadAsStringAsync()

        // Configure JSON options (case-insensitive is important)
        let opts = JsonSerializerOptions(PropertyNameCaseInsensitive = true)

        // Deserialize into list<Scids>
        let scids =
            JsonSerializer.Deserialize<string list>(body, opts)

        return scids
    }

// Repsonse map
type ScidVariable =
    { Key: JsonElement
      Value: JsonElement }
type ScResponse =
    { variables: ScidVariable[] }

let tryGetString (e: JsonElement) =
    match e.ValueKind with
    | JsonValueKind.String -> Some(e.GetString())
    | JsonValueKind.Number -> Some(e.GetRawText())
    | _ -> None

let tryGetVar (vars: ScidVariable array) (name: string) =
    vars
    |> Array.tryPick (fun v ->
        if tryGetString v.Key = Some name then
            tryGetString v.Value
        else None)

let GetSCIDVariableDetailsAtTopoheight (scid: string, height: string) =
    task {
        let url =
            $"http://localhost:8080/GetSCIDVariableDetailsAtTopoheight?scid={scid}&height={height}"

        let! response = http.GetAsync(url)
        response.EnsureSuccessStatusCode() |> ignore

        let! body = response.Content.ReadAsStringAsync()

        let opts = JsonSerializerOptions(PropertyNameCaseInsensitive = true)
        let vars =
            JsonSerializer.Deserialize<ScidVariable[]>(body, opts)
            |> fun v -> if isNull v then [||] else v

        return vars
    }

// Gets the latest
let GetSC (scid: string) =
    task {
        let url =
            $"http://localhost:8080/GetSC?scid={scid}"

        let! response = http.GetAsync(url)
        response.EnsureSuccessStatusCode() |> ignore

        let! body = response.Content.ReadAsStringAsync()

        let opts = JsonSerializerOptions(PropertyNameCaseInsensitive = true)
        let scResponse =
            JsonSerializer.Deserialize<ScResponse>(body, opts)
        return scResponse.variables
    }
