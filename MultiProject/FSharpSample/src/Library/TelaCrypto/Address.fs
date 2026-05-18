// Get point from address
namespace TelaCrypto
open System.Numerics

module Address =
    let decodeDeroAddressToPubKeyBytes (addr: string) =
        let bytes = Bech32.decodeToBytes addr
        bytes.[1..33]   // 32‑byte public key


    let decompressG1 (xb: byte[]) : G1.CurvePoint =
        if xb.Length <> 33 then
            failwithf "Expected 33-byte compressed key, got %d" xb.Length

        // 1. Extract X (first 32 bytes, big-endian)
        let xBytes = xb.[0..31]
        let x =
            let le = Array.rev xBytes
            BigInteger(Array.append le [|0uy|])

        // 2. Compute y² = x³ + 3 mod p
        let y2 =
            (x * x % Field.p |> fun t -> t * x % Field.p |> fun t -> (t + 3I) % Field.p)

        // 3. Compute both square roots
        let y1 = Field.sqrt y2
        let y2' = Field.sub Field.p y1

        // 4. Determine smaller/larger
        let smaller, larger =
            if y1 < y2' then y1, y2' else y2', y1

        // 5. Selector byte
        let selector = xb.[32]

        let y =
            if selector = 0uy then smaller else larger

        { X = x; Y = y; Z = 1I; T = 1I }





    let decodeDeroAddressToG1 (addr: string) =
        //printfn "Payload length: %d" (Bech32.decodeToBytes addr).Length
        //printfn "Payload hex: %s" (BitConverter.ToString(Bech32.decodeToBytes addr))  
        
        let pubBytes = decodeDeroAddressToPubKeyBytes addr
        //printfn "F# compressed pubkey: %s" (BitConverter.ToString(pubBytes))
    // printfn "%A" pubBytes
        

        decompressG1 pubBytes