namespace TelaCrypto

open System.Numerics
open System.Globalization
module Field =

    let p =
        BigInteger.Parse(
            "30644e72e131a029b85045b68181585d97816a916871ca8d3c208c16d87cfd47",
            NumberStyles.HexNumber
        )

    // bn256 quirk: scalar field order == p
    let order = p
    //let order = p   // bn256 scalar field order (same as modulus)

    let inline modp (x: BigInteger) =
        let r = x % p
        if r.Sign < 0 then r + p else r

    let add a b = modp (a + b)
    let sub a b = modp (a - b)
    let mul a b = modp (a * b)
    let square a = modp (a * a)
    // Compute modular square root using p ≡ 3 mod 4
    let sqrt a =
        // exponent = (p + 1) / 4
        let exp = (p + BigInteger.One) >>> 2
        BigInteger.ModPow(a, exp, p)

    // Fermat inverse: a^(p-2) mod p
    let inv a =
        BigInteger.ModPow(a, p - BigInteger(2), p)

    
