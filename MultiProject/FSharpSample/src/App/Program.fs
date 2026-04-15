// For more information see https://aka.ms/fsharp-console-apps
open System
open System.Diagnostics
open System.Net
open System.IO
open Gnomon
open Tela
open Dvm
open System.Net.Http
open System.Threading.Tasks

let waitForApi (url: string) =
    task {
        use client = new HttpClient()
        let mutable ready = false

        while not ready do
            try
                let! resp = client.GetAsync(url)
                if resp.IsSuccessStatusCode then
                    ready <- true
                else
                    do! Task.Delay(100)
            with _ ->
                do! Task.Delay(100)

        return ready
    }


let listener = new HttpListener()

let handleTelaRequest (context: HttpListenerContext,port:int) =
    
    task {            
        let path = context.Request.Url.LocalPath
        if isTelaProtocolRequest path then
            do! handleTelaProtocol context
        elif portToScid.ContainsKey port then
            printfn "handling instance request on port %d" port
            do! handleInstanceFileRequest context
        else
            context.Response.StatusCode <- 404
            context.Response.Close()
    }

let handleStartingRequests path context =
    task {
        if path = "/search" then
            return! search context

        elif path.StartsWith("/tela/open/") then
            return! handleTelaProtocol context

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
        | p when p >= 8082 && p <= 8090 ->
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
    listener.Prefixes.Add("http://localhost:8082/")
    listener.Prefixes.Add("http://localhost:8083/")
    listener.Prefixes.Add("http://localhost:8084/")
    listener.Prefixes.Add("http://localhost:8085/")
    listener.Prefixes.Add("http://localhost:8086/")
    listener.Prefixes.Add("http://localhost:8087/")
    listener.Prefixes.Add("http://localhost:8088/")
    listener.Prefixes.Add("http://localhost:8089/")
    listener.Prefixes.Add("http://localhost:8090/")
    listener.Start()
    let url = "http://localhost:8081/search"

    // Wait for API to be ready
    waitForApi url
    |> Async.AwaitTask
    |> Async.RunSynchronously
    |> ignore

    // Launch browser
    Process.Start(ProcessStartInfo(url, UseShellExecute = true)) |> ignore

    loop().GetAwaiter().GetResult()
    0








