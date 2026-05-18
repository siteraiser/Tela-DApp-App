namespace TelaCrypto

module Bech32 =
    let private charset = "qpzry9x8gf2tvdw0s3jn54khce6mua7l"

    let private decode (addr: string) =
        let pos = addr.LastIndexOf '1'
        if pos < 1 then failwith "Invalid bech32"
        let hrp = addr.Substring(0, pos)
        let data = addr.Substring(pos + 1)
        let values =
            data.ToCharArray()
            |> Array.map (fun c ->
                let i = charset.IndexOf(c)
                if i < 0 then failwith "Invalid bech32 char"
                i)
        hrp, values

    // Convert 5‑bit groups → 8‑bit bytes
    let convertBits (data: int[]) =
        let mutable acc = 0
        let mutable bits = 0
        let mutable out = ResizeArray<byte>()
        for v in data do
            acc <- (acc <<< 5) ||| v
            bits <- bits + 5
            if bits >= 8 then
                bits <- bits - 8
                out.Add(byte ((acc >>> bits) &&& 0xFF))
        out.ToArray()

    let decodeToBytes (addr: string) =
        let _, values = decode addr
        let dataNoChecksum = values.[0 .. values.Length - 7]   // drop last 6 checksum values
        convertBits dataNoChecksum