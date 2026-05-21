module Tela

open System
open System.Net
open System.IO
open TelaCrypto
open TelaCrypto.Signature
open Globalstate
open Security
open Gnomon
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
           

            let mutable title = tryGetVar vars "nameHdr"
            if title = None then 
                title <- tryGetVar vars "var_header_name"

            let durl =  tryGetVar vars "dURL" |> Option.defaultValue ""
            let isShards = durl.EndsWith(".shards")   
            if isShards then
                title <- None
                
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
                Some { scid = value; file = None; isGzip = false; mime = ""; bytes = [||]; verified = false }
            | _ -> None)   

    { rootscid = rootscid; version = version; owner = owner; docs = docs }



// Sharding
let getShardList vars =
    let vars = vars |> Array.toList
    vars
    |> List.choose (fun v ->
        match tryGetString v.Key, tryGetString v.Value with
        | Some key, Some value when key.StartsWith("DOC") ->
            Some (key, value)
        | _ -> None)
    |> List.sortBy (fun (key, _) ->
        let suffix = key.Substring(3)
        match System.Int32.TryParse suffix with
        | true, n -> n
        | _ -> 0)


let trimLeadingSlash (input: string) =
    if String.IsNullOrEmpty(input) then
        input // Return as-is if null or empty
    else
        input.TrimStart('/')
let trimTrailingSlash (input: string) =
    if String.IsNullOrEmpty(input) then
        input // Return as-is if null or empty
    else
        input.TrimEnd('/')
let checkSignature (sccode: string option) (vars: ScidVariable array) =
   
    // Signature fields
    let owner        = tryGetVar vars "owner" |> Option.defaultValue "error"
    let cHex         = tryGetVar vars "fileCheckC" |> Option.defaultValue ""
    let sHex         = tryGetVar vars "fileCheckS" |> Option.defaultValue ""
    let docImmutable = owner = "anon"

    printfn "verifying address %s" owner

    if docImmutable then
        true
    else
        // Extract comment
        let testCodeBytes =
            sccode
            |> Option.bind extractDvmComment
            |> Option.map System.Text.Encoding.UTF8.GetBytes
            |> Option.defaultValue [||]

        match tryParseBigIntHex cHex, tryParseBigIntHex sHex with
        | Some c, Some s ->
            let pubKey = Address.decodeDeroAddressToG1 owner
            //printfn "Owner pubKey: %s" (pointToString pubKey)

            let ok = verifySignature pubKey c s testCodeBytes
            printfn "Signature valid? %b" ok
            ok

        | _ ->
            printfn "Invalid signature fields: cHex=%A sHex=%A" cHex sHex
            false


let extractBytes (sccode: string option) =
    match sccode |> Option.bind extractDvmComment with
    | None ->
        [||], false
    | Some content ->
        // Try Base64 decode first
        let isGzipBase64 =
            try
                let b = Convert.FromBase64String(content)
                b.[0] = 0x1Fuy && b.[1] = 0x8Buy
            with _ -> false
        
        if isGzipBase64 then
            Convert.FromBase64String(content), true
        else
            Encoding.UTF8.GetBytes(content), false



type ShardResult =
    { valid : bool
      verified : bool
      file     : string option
      document : byte[]
      isGzip   : bool }

let getShardedDoc (indexVars: ScidVariable array) = task {

    let shards = getShardList indexVars   // (DOCkey, SCID) sorted

    let mutable shardInfos = []
    let mutable name = None
    let mutable valid = true
    for (_, scid) in shards do
        let! vars = GetSC scid

        let sccode = tryGetVar vars "C"
        let verified = checkSignature sccode vars
        let comment = sccode |> Option.bind extractDvmComment
        name <- tryGetVar vars "var_header_name"
        if valid then
            valid <- docContainsRequiredFunctions vars

        shardInfos <- (verified, comment, vars) :: shardInfos

    let shardInfos = List.rev shardInfos

    let allVerified = 
        shardInfos |> List.forall (fun (v,_,_) -> v)
        


    // 1. Combine raw fragment strings
    let combinedString =
        shardInfos
        |> List.choose (fun (_,comment,_) -> comment)
        |> String.concat ""

    // 2. Decode Base64 ONCE
    let decoded =
        try Convert.FromBase64String(combinedString)
        with _ -> Encoding.UTF8.GetBytes(combinedString)

    // 3. Detect gzip ONCE
    let isGzip =
        decoded.Length > 2 &&
        decoded.[0] = 0x1Fuy &&
        decoded.[1] = 0x8Buy

    // 4. Infer filename
    let cleanShardFilename (name: string) =
        // Handle double extensions like ".js.gz", ".css.gz", ".html.gz"
        let lastDot = name.LastIndexOf('.')
        if lastDot <= 0 then name else

        // If it ends with ".gz", step back one extension
        let mainExtDot =
            if name.EndsWith(".gz") then
                name.LastIndexOf('.', lastDot - 1)
            else
                lastDot

        if mainExtDot <= 0 then name else

        // Look for "-<digits>" before the main extension
        let dash = name.LastIndexOf('-', mainExtDot)
        if dash <= 0 then name else

        let suffix = name.Substring(dash + 1, mainExtDot - dash - 1)
        if suffix |> Seq.forall System.Char.IsDigit then
            // Remove "-<digits>"
            name.Remove(dash, mainExtDot - dash)
        else
            name

    let cleanName = name |> Option.defaultValue "" 

    let cleanName = cleanShardFilename cleanName

    return {
        valid    = valid
        verified = allVerified        
        file     = if cleanName = "" then None else Some cleanName 
        document = decoded
        isGzip   = isGzip
    }
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

let enrichDocMap (dm: DocMap) =
    task {
        let mutable newDocs = []

        for d in dm.docs do
            let! vars = GetSC d.scid

            // If SCID is invalid or missing, return a stub entry
            if isNull vars then
                printfn "Error loading SCID %s" d.scid
                newDocs <- { d with file = None; isGzip = false; mime = "";  bytes = [||]; verified = false; } :: newDocs
            else
                printfn "Enriching SCID %s" d.scid
                
                // Helper: safe lookup
                let get key = tryGetVar vars key

                // Metadata               
                let mutable subdir   = get "subDir" |> Option.map trimLeadingSlash |> Option.map trimTrailingSlash
                let sccode   = get "C"

                let filename =
                    [ "nameHdr"; "var_header_name" ]
                    |> List.tryPick get
                    |> Option.map trimLeadingSlash

                
                
                let mutable file = None                    
                let mutable verified = false
                let mutable rawBytes = [||]
                let mutable isGzip = false
                // branch here 

                let isSharded =
                    tryGetVar vars "DOC1"
                    |> Option.isSome

                
                if not(isSharded) then
                    // Normal Document
                    printf "NORMAL DOC\n"
                    // Signature verification
                    verified <- checkSignature sccode vars // owner cHex sHex
                    // Build file path
                    file <- [ subdir; filename ]
                    |> List.choose id
                    |> String.concat "/"
                    |> trimLeadingSlash
                    |> function
                        | null | "" -> None
                        | s -> Some s

                    // Extract embedded content bytes
                    let b, g = extractBytes sccode
                    rawBytes <- b
                    isGzip <- g   
                else
                    // Shards Index, needs to be assembled
                    printf "SHARDED DOC\n"
                    let! r = getShardedDoc vars

                    if not r.valid && indexContainsRequiredFunctions vars then
                        printfn "Error invalid shard, shards sc %s" d.scid
                        newDocs <- { d with file = None; isGzip = false; mime = "";  bytes = [||]; verified = false; } :: newDocs

                    verified <- r.verified
                    file <- r.file
                    rawBytes <- r.document
                    isGzip <- r.isGzip

                    
                    //maybe consider subdir as an option, then fall back...
                    let folder = get "dURL" |> Option.map trimLeadingSlash |> Option.map trimTrailingSlash 
                    file <-
                    [ folder; file ]
                    |> List.choose id
                    |> function
                        | [] -> None
                        | parts -> Some (String.concat "/" parts)


                printfn "File: %O" file   
                  
                
                // Determine MIME type
                let mime =
                    match file with
                    | Some f when isGzip && f.EndsWith(".gz") ->
                        getMime (Some (f.Substring(0, f.Length - 3)))
                    | Some f ->
                        getMime (Some f)
                    | None ->
                        "application/octet-stream" // maybe default to something else

                printfn "Loaded SCID %s: file=%A  C_length=%A" d.scid file (sccode |> Option.map String.length)

                newDocs <-
                    { d with                        
                        file     = file                        
                        bytes    = rawBytes
                        verified = verified
                        mime     = mime
                        isGzip   = isGzip
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



let respond404 (context: HttpListenerContext) (msg: string) =
    task {
        let response = context.Response
        use w = new StreamWriter(response.OutputStream)
        response.StatusCode <- 404
        do! w.WriteAsync(msg)
        response.Close()
    }



let serve (ctx: HttpListenerContext) (entry: DocEntry) =
    let res = ctx.Response
    res.ContentType <- entry.mime
    if entry.isGzip then
        res.AddHeader("Content-Encoding", "gzip")
    res.ContentLength64 <- int64 entry.bytes.Length
    task {
        do! res.OutputStream.WriteAsync(entry.bytes, 0, entry.bytes.Length)
        res.OutputStream.Close()
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

 