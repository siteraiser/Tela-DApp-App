// For more information see https://aka.ms/fsharp-console-apps
open System.Diagnostics
open System.Net
open Globalstate
open Security
open Tela
open System.Net.Http
open System.Threading.Tasks
open System.IO
// Build command:dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true
let listener = new HttpListener()


let handleOpenRequest (context: HttpListenerContext) =
    task {
        let path = context.Request.Url.LocalPath

        match extractScidFromOpenPath path with
        | None ->
            return! respond404 context "Invalid SCID open path"

        | Some scid ->
            // Build or fetch DocMap
            let! enrichedDocMap = handleOpen scid

            // Pick or reuse port
            let port =
                match scidToPort.TryGetValue scid with
                | true, p -> p
                | false, _ ->
                    let p = pickPort()
                    listener.Prefixes.Add($"http://localhost:{p}/")
                    scidToPort.[scid] <- p
                    portToScid.[p] <- scid
                    p

            // Register instance
            portToInstance.[port] <- { scid = scid; docMap = enrichedDocMap }

            // Launch browser
            let url = $"http://localhost:{port}/"
            Process.Start(ProcessStartInfo(url, UseShellExecute = true)) |> ignore

            // Respond to AJAX
            let response = context.Response
            use writer = new StreamWriter(response.OutputStream)
            do! writer.WriteAsync($"{{'scid':'{scid}'}}")
            do! writer.FlushAsync()
            response.Close()
    }


let waitForApi (url: string) =
    task {
        use client = new HttpClient()
        let mutable ready = false

        while not ready do
            try
                let! resp = client.GetAsync(url)
                printfn "%A"  resp
                if resp.IsSuccessStatusCode then
                    ready <- true
                else
                    do! Task.Delay(100)
            with _ ->
                do! Task.Delay(100)

        return ready
    }



let handleTelaRequest (context: HttpListenerContext,port:int) =
    
    task {            
        let path = context.Request.Url.LocalPath
        if portToScid.ContainsKey port then
            printfn "handling instance request on port %d" port
            do! handleInstanceFileRequest context
        else
            context.Response.StatusCode <- 404
            context.Response.Close()
    }



let isValid (context: HttpListenerContext) = 
    match context.Request.Headers.["X-Launcher-Token"] with
    | null -> false
    | token -> validate token

let handleStartingRequests path context =
    task {
        if path = "/search" then
            return! search context
        elif path.StartsWith("/tela/open/") && isValid context then      
            return! handleOpenRequest context
        else
            return! respond404 context "Unknown starting route"
    }

let rec loop () =
    task {
        let! context = listener.GetContextAsync()

        let port = context.Request.Url.Port
        let path = context.Request.Url.LocalPath

        match port with
        | p when p = 8081 ->
            printfn "ROUTER: Starting request on port 8081, path=%s" path
            do! handleStartingRequests path context

        | p when portToScid.ContainsKey p ->
            printfn "ROUTER: Instance request on port %d, path=%s" p path
            do! handleTelaRequest (context, port)

        | _ ->
            printfn "ROUTER: Unknown port %d" port
            context.Response.StatusCode <- 404
            context.Response.Close()
        return! loop ()
    }


[<EntryPoint>]
let main argv =
    printfn "Starting App on localhost:8081 (Ensure the Gnomon API api is running on port 8080). The browser will launch when ready."
    listener.Prefixes.Add("http://localhost:8081/")
    listener.Start()
    let url = "http://localhost:8081/search"

    // Wait for API to be ready
    waitForApi "http://localhost:8080/GetLastIndexHeight"
    |> Async.AwaitTask
    |> Async.RunSynchronously
    |> ignore

    // Launch browser
    Process.Start(ProcessStartInfo(url, UseShellExecute = true)) |> ignore

    loop().GetAwaiter().GetResult()
    0





