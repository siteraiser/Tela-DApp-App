module Tela

open System
open System.Net
open System.IO
open System.Diagnostics
open System.Collections.Concurrent
open Gnomon
open Json
open Htmlcontent
open Dvm
open System.Text

type DocEntry =
    { scid: string
      file: string option
      doctype: string option 
      sccode: string option }

type DocMap =
    { rootscid: string
      docs: DocEntry list }

type InstanceState =
    { scid     : string
      docMap    : DocMap }

module DocStore =

    let mutable private store : Map<string, DocMap> = Map.empty

    let tryGet scid =
        store |> Map.tryFind scid

    let add scid docMap =
        store <- store.Add(scid, docMap)



let search (context: HttpListenerContext) =
    task {
        let response = context.Response
        use writer = new StreamWriter(response.OutputStream)

        let! html = index []   // or whatever args you want
        let html = searchHTMLOpen + html + searchHTMLClose
        
        do! writer.WriteAsync(html)
        do! writer.FlushAsync()

        response.Close()
    }

let isTelaProtocolRequest (path: string) =
    path.StartsWith("/tela/open/")

let isActiveInstanceRequest (ctx: HttpListenerContext) =
    ctx.Request.Url.Port <> 8081



let buildDocMap rootscid vars =
    let docs =
        vars
        |> List.choose (fun v ->
            let key = tryGetString v.Key
            let value = tryGetString v.Value
            match key, value with
            | Some key, Some value when key.StartsWith("DOC") -> Some { scid = value; doctype = None; file = None; sccode = None }
            | _ -> None)

    { rootscid = rootscid; docs = docs }


let tryGet (key: string, vars: ScidVariable array) =
    vars
    |> Array.tryFind (fun v ->
        match jsonToOptString v.Key with
        | Some k -> k = key
        | None -> false)
    |> Option.bind (fun v -> jsonToOptString v.Value)


let trimLeadingSlash (input: string) =
    if String.IsNullOrEmpty(input) then
        input // Return as-is if null or empty
    else
        input.TrimStart('/')

let enrichDocMap (dm: DocMap) =
    task {
        let mutable newDocs = []

        for d in dm.docs do
            let! vars = GetSC d.scid

            let filename =
                tryGet ("nameHdr", vars)
                |> Option.orElse (tryGet ("dURL", vars))
            let subdir = tryGet ("subDir", vars)
            let doctype  = tryGet ("docType", vars)
            let sccode  = tryGet ("C", vars)   // ← NEW
            let full =
                [ subdir; filename ]
                |> List.choose id
                |> String.concat "/"

            let toStringOption (input: string) : string option =
                match input with
                | null -> None                      // Handle null
                | s when String.IsNullOrWhiteSpace s -> None // Handle empty/whitespace
                | s -> Some s       
                     
            let full = trimLeadingSlash full
            let file = toStringOption full 
          
            printfn "Loaded SCID %s: file=%A doctype=%A C_length=%A"
                d.scid file doctype (sccode |> Option.map String.length)

            let updated =
                { d with
                    file = file
                    doctype  = doctype 
                    sccode =  sccode }

            newDocs <- updated :: newDocs

        return { dm with docs = List.rev newDocs }
    }



let handleOpen scid = task {
    match DocStore.tryGet scid with
    | Some dm ->
        printfn "Serving cached doc map for %s" scid
        return dm

    | None ->
        let! vars = GetSCIDVariableDetailsAtTopoheight (scid, "100000000")
        let vars = vars |> Array.toList
        let dm = buildDocMap scid vars
        let! enriched = enrichDocMap dm

        printfn "OPEN: Storing DocMap under SCID %s" scid
        DocStore.add scid enriched

        return enriched
}


let scidToPort = ConcurrentDictionary<string, int>()
let portToScid = ConcurrentDictionary<int, string>()
let portToInstance = ConcurrentDictionary<int, InstanceState>()
//let instances = Dictionary<int, InstanceState>()

let instances = ConcurrentDictionary<int, InstanceState>()


let pickPort () =
    [8082..8090]
    |> List.tryFind (fun p -> not (portToScid.ContainsKey p))

// handles the ajax request from the initial search page
let handleTelaProtocol (context: HttpListenerContext) =
    task {
        let path = context.Request.Url.LocalPath
        let response = context.Response
        use writer = new StreamWriter(response.OutputStream)

        printfn "TELA PROTOCOL: Raw path = %s" path

        let prefix = "/tela/open/"
        let scid =
            if path.StartsWith(prefix) then
                path.Substring(prefix.Length)
            else
                printfn "TELA PROTOCOL: Invalid open path"
                ""

        printfn "TELA PROTOCOL: Extracted SCID = %s" scid
        printfn "TELA PROTOCOL: Launching instance..."

        // Load DocMap (async)
        let! enrichedDocMap = handleOpen scid

        // Pick or reuse a port
        let port =
            match scidToPort.TryGetValue scid with
            | true, p -> p
            | false, _ ->
                match pickPort () with
                | Some p ->
                    scidToPort.[scid] <- p
                    portToScid.[p] <- scid
                    p
                | None ->
                    failwith "No available port"

        // Create instance state
        let instance =
            { scid = scid
              docMap = enrichedDocMap }

        portToInstance.[port] <- instance

        printfn "Instance ready on port %d" port

        // Launch browser AFTER instance is ready
        let url = $"http://localhost:{port}/"
        Process.Start(ProcessStartInfo(url, UseShellExecute = true)) |> ignore

        // Respond to AJAX caller
        do! writer.WriteAsync("{'scid':'" + scid + "'}")
        do! writer.FlushAsync()

        response.Close()
    }

let respond404 (context: HttpListenerContext) (msg: string) =
    task {
        let response = context.Response
        use w = new StreamWriter(response.OutputStream)
        response.StatusCode <- 404
        do! w.WriteAsync(msg)
        response.Close()
    }
let getMime  (fileName : string option) = 
    match fileName with
    | Some f when f.EndsWith(".html") -> "text/html"
    | Some f when f.EndsWith(".css")  -> "text/css"
    | Some f when f.EndsWith(".js")   -> "application/javascript"
    | Some f when f.EndsWith(".json") -> "application/json"
    | Some f when f.EndsWith(".txt")  -> "text/plain"
    | _ -> "text/plain"

let serve (context: HttpListenerContext) (entry: DocEntry) =
    let response = context.Response
    printfn "entry: %A" (entry)
    printfn "SERVE: Serving file %A for SCID %s \n Code %A" entry.file entry.scid entry.sccode
    // 1. Load SCID source. 
    let sccode  = entry.sccode |> optStringToRawJson
    let bytes, isGzip =
        match extractDvmComment sccode with
        | None ->
            Encoding.UTF8.GetBytes("No embedded content found"), false

        | Some content ->
            let isGzipBase64 =
                try
                    let b = Convert.FromBase64String(content)
                    b.[0] = 0x1Fuy && b.[1] = 0x8Buy
                with _ -> false
            
            if isGzipBase64 then
                Convert.FromBase64String(content), true
            else
                Encoding.UTF8.GetBytes(content), false
    if isGzip then
        response.AddHeader("Content-Encoding", "gzip")
    response.ContentLength64 <- int64 bytes.Length

    task {       
        do! response.OutputStream.WriteAsync(bytes, 0, bytes.Length)
        response.OutputStream.Close()
        return ()
    }



let handleInstanceFileRequest (context: HttpListenerContext) =
    task {
        let port = context.Request.Url.Port
        let raw = context.Request.Url.LocalPath

        let path =
            match raw with
            | "/" -> "index.html"
            | p -> p.TrimStart('/')

        printfn "RAW PATH: '%s'" context.Request.Url.LocalPath
        printfn "NORMALIZED PATH: '%s'" path

        let response = context.Response
        printfn "INSTANCE: Request on port %d, path=%s" port path

        match portToScid.TryGetValue port with
        | false, _ ->
            printfn "INSTANCE: No SCID mapped to port %d" port
            return! respond404 context "Unknown instance port"

        | true, scid ->
            printfn "INSTANCE: Port %d maps to SCID %s" port scid
            match portToInstance.TryGetValue port with
            | false, _ ->
                printfn "INSTANCE: No instance for port %d" port
                return! respond404 context "Unknown instance port"

            | true, instance ->
                let dm = instance.docMap
                match path with
                | file -> // try to find the file
                    match dm.docs |> List.tryFind (fun d -> d.file = Some file) with
                    | Some entry -> return! serve context entry
                    | None -> 
                        let file = file + ".gz" // try to find a gzip version
                        match dm.docs |> List.tryFind (fun d -> d.file = Some file) with
                        | Some entry -> return! serve context entry
                        | None ->
                            match dm.docs with
                            | [single] -> return! serve context single
                            | _ -> return! respond404 context "No index.html and multiple docs"

    }

 