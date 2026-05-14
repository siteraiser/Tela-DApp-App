module Tela

open System
open System.Net
open System.IO
open Globalstate
open Gnomon
open Json
open Htmlcontent
open Dvm
open System.Text


let requiredFunction = """Function Rate(r Uint64) Uint64
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
let normalize (s: string) =
    s.Replace("\r\n", "\n")
     .Replace("\r", "\n")
     .Split('\n')
     |> Array.map (fun line -> line.Trim())
     |> Array.filter (fun line -> line <> "")
     |> String.concat "\n"

let containsRequiredFunction vars =
    match tryGetVar vars "C" with
    | None -> false
    | Some sccode ->
        let a = normalize sccode
        let b = normalize requiredFunction
        a.Contains b

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
            let nameHdr = tryGetVar vars "nameHdr"
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

                if  docs.Length = 0 || not(containsRequiredFunction vars) then
                    printfn "No documents found"
                else
                    // Build the link
                    results.Add($"<div><a href='#' data-scid='{scid}'>{title}</a>")
                    let descrHdr = tryGetVar vars "descrHdr"
                    match descrHdr with
                    | None ->
                        results.Add($"</div>")
                    | Some descrHdr ->
                        results.Add($"{descrHdr}</div>")

        // Return all links joined by newlines
        return "<div>" + String.concat "</div><div>" results + "</div>" 
    }



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
                tryGetVar vars "nameHdr" 
               // |> Option.orElse (tryGet ("dURL", vars))
            let subdir = tryGetVar vars "subDir"
            let doctype  = tryGetVar vars "docType"
            let sccode  = tryGetVar vars "C"
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


let extractScidFromOpenPath (path: string) =
    let prefix = "/tela/open/"
    if path.StartsWith(prefix) then
        Some (path.Substring(prefix.Length))
    else
        None

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
    | Some f when f.EndsWith(".svg")  -> "image/svg+xml"
    | Some f when f.EndsWith(".txt")  -> "text/plain"
    | _ -> "text/plain"

let serve (context: HttpListenerContext) (entry: DocEntry) =
    let response = context.Response
    printfn "entry: %A" (entry)
    printfn "SERVE: Serving file %A for SCID %s \n Code %A" entry.file entry.scid entry.sccode
    // 1. Load SCID source. 
    let sccode  = entry.sccode |> optStringToRawJson
    //
    let bytes, isGzip =
        match extractDvmComment sccode with
        | None ->
            response.StatusCode <- 500
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
        let realFile =
            entry.file |> Option.map (fun f -> if f.EndsWith(".gz") then f.Substring(0, f.Length - 3) else f)
        response.ContentType <- getMime realFile    
        response.AddHeader("Content-Encoding", "gzip")
    else
        response.ContentType <- getMime entry.file   
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

 