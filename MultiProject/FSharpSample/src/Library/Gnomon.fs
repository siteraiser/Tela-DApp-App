module Gnomon

open System.Net.Http
open System.Text.Json



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
let tryGetString (e: JsonElement) =
    match e.ValueKind with
    | JsonValueKind.String -> Some(e.GetString())
    | JsonValueKind.Number -> Some(e.GetRawText())
    | _ -> None


let index args =
    task {
        // First call
        let! height = GetLastIndexHeight
        printfn "Output: %A" height
       
        // Second call
        let! scids = getSCIDsByTags ["telaVersion"]       

        let results = ResizeArray<string>()

        for scid in scids do
            let! vars = GetSCIDVariableDetailsAtTopoheight (scid, height)

            // Extract nameHdr if present
            let nameHdr =
                vars
                |> Array.tryPick (fun v ->
                    match tryGetString v.Key, tryGetString v.Value with
                    | Some "nameHdr", Some value when value <> "" -> Some value
                    | _ -> None)

            match nameHdr with
            | None ->
                printfn "SCID %s has no title" scid

            | Some title ->
                printfn "SCID %s → Title: %s" scid title

                // Collect DOC1, DOC2, DOC3...
                let docs =
                    vars
                    |> Array.choose (fun v ->
                        match tryGetString v.Key, tryGetString v.Value with
                        | Some key, Some value when key.StartsWith("DOC") -> Some (key, value)
                        | _ -> None)

                if docs.Length = 0 then
                    printfn "  No documents found"
                else
            
                    let orderedDocs =
                        docs |> Array.sortBy fst
                    for (key, docScid) in orderedDocs do
                        printfn "  %s → %s" key docScid
                   
                // Build the link
                results.Add($"<a href='#' data-scid='{scid}'>{title}</a>")

        // Return all links joined by newlines
        return String.concat "<br>" results
    }