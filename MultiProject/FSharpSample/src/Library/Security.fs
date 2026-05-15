module Security
open System
open System.Security.Cryptography
open System.Text.Encodings.Web

// Use a token to ensure launch requests are valid
let private token =
    let bytes = Array.zeroCreate<byte> 32
    RandomNumberGenerator.Fill(bytes)
    Convert.ToBase64String(bytes)

let getToken() = token

let validate incoming = incoming = token

// Safely output text for display on search page
let encodeForHtml (value: string) =
    if isNull value then ""
    else HtmlEncoder.Default.Encode value

