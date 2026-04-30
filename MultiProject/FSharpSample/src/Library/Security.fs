module Security
open System
open System.Security.Cryptography

let private token =
    let bytes = Array.zeroCreate<byte> 32
    RandomNumberGenerator.Fill(bytes)
    Convert.ToBase64String(bytes)

let getToken() = token

let validate incoming = incoming = token