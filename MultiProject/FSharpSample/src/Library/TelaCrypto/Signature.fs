namespace TelaCrypto

module Signature =
    open System
    open System.Numerics
    open System.Text
    open System.Globalization
    open G1

    // You can adjust this later to match how Dero stores signatures.
    type Signature =
        { R: CurvePoint      // curve point
          S: BigInteger // scalar
        }

    type PublicKey = Point

    let curveOrder =
        BigInteger.Parse(
            "30644e72e131a029b85045b68181585d2833e84879b9709143e1f593f0000001",
            NumberStyles.HexNumber
        )
    let bigIntFromBigEndian (bytes: byte[]) =
        let extended = Array.append [| 0uy |] bytes  // force positive
        extended |> Array.rev |> BigInteger

    let hashToScalar (bytes: byte[]) =
        let digest = Keccak256.compute bytes  // must match Go exactly
        let bi = bigIntFromBigEndian digest
        BigInteger.Remainder(bi, curveOrder)


    let verifySignature (pubKey: G1.CurvePoint) (c: BigInteger) (s: BigInteger) (message: byte[]) =
        //printfn "F# s: %A" s
        //printfn "F# c: %A" c

        let sG    = G1.scalarMult G1.G s
        let cP    = G1.scalarMult pubKey c
        let cPneg = G1.neg cP
        let R     = G1.add sG cPneg

        //printfn "F# sG: %s" (pointToString sG)
        //printfn "F# (-c)P: %s" (pointToString cPneg)
        //printfn "F# R': %s" (pointToString R)

        let serialize =
            let pubStr = pointToString pubKey
            let rStr   = pointToString R
            let msgHex =
                message
                |> Array.map (fun b -> b.ToString("x2"))
                |> String.concat ""
            Encoding.ASCII.GetBytes(pubStr + rStr + msgHex)

        let ByteToHex bytes =
            bytes
            |> Array.map (fun (x : byte) -> System.String.Format("{0:X2}", x))
            |> String.concat System.String.Empty

        //printfn "F# serialize hex': %s" (ByteToHex serialize)



        let cCalculated = hashToScalar serialize
        //printfn "F# c': %A" cCalculated
        c = cCalculated



    let hexToBigInt (hex: string) =
        BigInteger.Parse(hex, NumberStyles.AllowHexSpecifier)


    let tryParseBigIntHex (hex: string) =
        try
            if String.IsNullOrWhiteSpace hex then None
            else
                // strip 0x prefix if present
                let clean =
                    if hex.StartsWith("0x") then hex.Substring(2)
                    else hex

                // pad odd-length hex
                let padded =
                    if clean.Length % 2 = 1 then "0" + clean
                    else clean

                Some (BigInteger.Parse(padded, NumberStyles.HexNumber))
        with _ ->
            None




