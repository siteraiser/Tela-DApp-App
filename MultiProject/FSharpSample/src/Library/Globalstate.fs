module Globalstate

open System.Collections.Concurrent


type DocEntry =
    { scid: string
      file: string option
      bytes: byte[]
      mime: string
      isGzip: bool
      verified: bool }

type DocMap =
    {   rootscid: string
        version: string option
        owner: string option
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

let scidToPort = ConcurrentDictionary<string, int>()
let portToScid = ConcurrentDictionary<int, string>()
let portToInstance = ConcurrentDictionary<int, InstanceState>()

let pickPort () =
    let tcp = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0)
    tcp.Start()
    let port = (tcp.LocalEndpoint :?> System.Net.IPEndPoint).Port
    tcp.Stop()
    port

