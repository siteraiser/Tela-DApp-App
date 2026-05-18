module Dvm

let extractDvmComment (source:string) =
    let startToken = "/*"
    let endToken = "*/"
    //printfn "SERVE: Extracting embedded content..."
    let startIdx = source.IndexOf(startToken)
    if startIdx < 0 then
        None
    else
        let contentStart = startIdx + startToken.Length
        let endIdx = source.IndexOf(endToken, contentStart)
        if endIdx < 0 then
            None
        else
            let raw = source.Substring(contentStart, endIdx - contentStart)
            //printfn "SERVE: embedded content... %A"  raw
            Some raw