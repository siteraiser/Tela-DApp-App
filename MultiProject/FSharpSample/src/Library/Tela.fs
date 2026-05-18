module Tela

open System
open System.Net
open System.IO
open TelaCrypto
open TelaCrypto.G1
open TelaCrypto.Signature
open Globalstate
open Security
open Gnomon
open Json
open Htmlcontent
open Dvm
open System.Text
open Telavalidation



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
            
            let title =
                [ "nameHdr"
                  "var_header_name" ]
                |> List.tryPick (fun key -> tryGetVar vars key)

            match title with
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

                if  docs.Length = 0 || not(indexContainsRequiredFunctions vars) then
                    printfn "No documents found"
                else
                    let safeTitle = encodeForHtml title
                    let ownerTxt =
                        match tryGetVar vars "owner" with
                        | None -> "anon"
                        | Some o -> o
                    // Build the link
                    results.Add($"<div><a href='#' data-scid='{scid}' data-owner='{ownerTxt}'>{safeTitle}</a>")

                    let description =
                        [ "descrHdr"
                          "var_header_description" ]
                        |> List.tryPick (fun key -> tryGetVar vars key)
                    match description with
                    | None ->
                        results.Add($"</div>")
                    | Some description ->
                        let safeDescrHdr = encodeForHtml description
                        results.Add($"{safeDescrHdr}</div>")

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

    let owner = tryGetVar vars "owner"
    let version = tryGetVar vars "telaVersion"
    let vars = vars |> Array.toList
    let docs =
        vars
        |> List.choose (fun v ->
            match tryGetString v.Key, tryGetString v.Value with
            | Some key, Some value when key.StartsWith("DOC") ->
                Some { scid = value; doctype = None; file = None; sccode = None; verified = false }
            | _ -> None)   

    { rootscid = rootscid; version = version; owner = owner; docs = docs }


let trimLeadingSlash (input: string) =
    if String.IsNullOrEmpty(input) then
        input // Return as-is if null or empty
    else
        input.TrimStart('/')


let checkSignature (message: byte[]) (address: string) (cHex: string) (sHex: string) =
    match tryParseBigIntHex cHex, tryParseBigIntHex sHex with
    | Some c, Some s ->
        let pubKey = Address.decodeDeroAddressToG1 address
        printfn "Owner pubKey: %s" (pointToString pubKey)

        let ok = verifySignature pubKey c s message
        printfn "Signature valid? %b" ok
        ok

    | _ ->
        printfn "Invalid signature fields: cHex=%A sHex=%A" cHex sHex
        false

let enrichDocMap (dm: DocMap) =
    task {
        let mutable newDocs = []

        for d in dm.docs do
            let! vars = GetSC d.scid

            // If SCID is invalid or missing, return a stub entry
            if isNull vars then
                printfn "Error loading SCID %s" d.scid
                newDocs <- { d with verified = false; file = None; doctype = None; sccode = None } :: newDocs

            else
                printfn "Enriching SCID %s" d.scid

                // Helper: safe lookup
                let get key = tryGetVar vars key

                // Metadata
                let filename =
                    [ "nameHdr"; "var_header_name" ]
                    |> List.tryPick get

                let subdir   = get "subDir"
                let doctype  = get "docType"
                let sccode   = get "C"

                // Signature fields
                let owner        = get "owner" |> Option.defaultValue "error"
                let cHex         = get "fileCheckC" |> Option.defaultValue ""
                let sHex         = get "fileCheckS" |> Option.defaultValue ""
                let mapOwner     = dm.owner |> Option.defaultValue "error"
                let docImmutable = owner = "anon"
                let mapImmutable = mapOwner = "anon"

                // Extract comment
                let testCodeBytes =
                    sccode
                    |> Option.bind extractDvmComment
                    |> Option.map System.Text.Encoding.UTF8.GetBytes
                    |> Option.defaultValue [||]

                // Signature verification
                let verified =
                    if docImmutable then
                        true
                    else
                        printfn "verifying address %s" owner
                        let ok = checkSignature testCodeBytes owner cHex sHex
                        ok || (docImmutable && docContainsRequiredFunctions vars)

                // Build file path
                let file =
                    [ subdir; filename ]
                    |> List.choose id
                    |> String.concat "/"
                    |> trimLeadingSlash
                    |> function
                        | null | "" -> None
                        | s -> Some s

                printfn "Loaded SCID %s: file=%A doctype=%A C_length=%A"
                    d.scid file doctype (sccode |> Option.map String.length)

                newDocs <-
                    { d with
                        file     = file
                        doctype  = doctype
                        sccode   = sccode
                        verified = verified
                    }
                    :: newDocs

        return { dm with docs = List.rev newDocs }
    }




let handleOpen scid = task {
    match DocStore.tryGet scid with
    | Some dm ->
        printfn "Serving cached doc map for %s" scid
        return dm

    | None ->
        let! vars = GetSCIDVariableDetailsAtTopoheight (scid, "100000000")
        //let vars = vars |> Array.toList
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
    //printfn "entry: %A" (entry)
    printfn "SERVE: Serving file %A for SCID %s \n " entry.file entry.scid //entry.sccode
    // 1. Load SCID source. 
    let sccode  = entry.sccode |> optStringToRawJson

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

 