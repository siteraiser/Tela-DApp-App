namespace TelaCrypto
module G1 =

    open System.Numerics
    open System.Globalization

    type CurvePoint =
        { mutable X: BigInteger
          mutable Y: BigInteger
          mutable Z: BigInteger
          mutable T: BigInteger }  // T = Z^2 when valid

    let zero =
        { X = 0I
          Y = 1I
          Z = 0I
          T = 0I }

    let G =
        { X = BigInteger.Parse("02eacfbf92b94015c9b0b3d901dae37ec68f74dea7e4484c76d505aade4ad7c0", NumberStyles.HexNumber)
          Y = BigInteger.Parse("1bb19d0d6bfcd78fc21da8d68dff1e8441cfa73d66cab25bb0315cf6d6433b5d", NumberStyles.HexNumber)
          Z = 1I
          T = 1I }

    // helpers
    let inline isInfinity (p: CurvePoint) = (p.Z = 0I)

    let clone (p: CurvePoint) =
        { X = p.X; Y = p.Y; Z = p.Z; T = p.T }

    let set (dst: CurvePoint) (src: CurvePoint) =
        dst.X <- src.X
        dst.Y <- src.Y
        dst.Z <- src.Z
        dst.T <- src.T

    // MakeAffine (Go's MakeAffine)
    let makeAffine (p: CurvePoint) =
        if p.Z = 1I then
            p.T <- 1I
        elif p.Z = 0I then
            p.X <- 0I
            p.Y <- 1I
            p.Z <- 0I
            p.T <- 0I
        else
            let zInv  = Field.inv p.Z
            let t     = Field.mul p.Y zInv
            let zInv2 = Field.mul zInv zInv

            p.X <- Field.mul p.X zInv2
            p.Y <- Field.mul t   zInv2
            p.Z <- 1I
            p.T <- 1I

    // Double (Go's dbl-2009-l.op3)
    let double (a: CurvePoint) : CurvePoint =
        if isInfinity a then clone a else
        let A = Field.mul a.X a.X
        let B = Field.mul a.Y a.Y
        let C = Field.mul B B

        let t  = Field.add a.X B
        let t2 = Field.mul t t
        let t  = Field.sub t2 A
        let t2 = Field.sub t C

        let d  = Field.add t2 t2
        let t  = Field.add A A
        let e  = Field.add t A
        let f  = Field.mul e e

        let t  = Field.add d d
        let x3 = Field.sub f t

        let z3 =
            let z = Field.mul a.Y a.Z
            Field.add z z

        let t  = Field.add C C
        let t2 = Field.add t t
        let t  = Field.add t2 t2
        let yTmp = Field.sub d x3
        let t2 = Field.mul e yTmp
        let y3 = Field.sub t2 t

        { X = x3; Y = y3; Z = z3; T = Field.mul z3 z3 }

    // Add (Go's add-2007-bl.op3)
    let add (a: CurvePoint) (b: CurvePoint) : CurvePoint =
        if isInfinity a then clone b
        elif isInfinity b then clone a
        else
            let z12 = Field.mul a.Z a.Z
            let z22 = Field.mul b.Z b.Z

            let u1  = Field.mul a.X z22
            let u2  = Field.mul b.X z12

            let t   = Field.mul b.Z z22
            let s1  = Field.mul a.Y t

            let t2  = Field.mul a.Z z12
            let s2  = Field.mul b.Y t2

            let h   = Field.sub u2 u1
            let xEqual = (h = 0I)

            let t_h2 = Field.add h h
            let i    = Field.mul t_h2 t_h2      // i = 4h^2
            let j    = Field.mul h i            // j = 4h^3

            let t_s  = Field.sub s2 s1
            let yEqual = (t_s = 0I)
            if xEqual && yEqual then
                double a
            else
                let r  = Field.add t_s t_s      // r = 2(s2-s1)
                let v  = Field.mul u1 i

                let t4 = Field.mul r r          // t4 = r^2
                let t  = Field.add v v
                let t6 = Field.sub t4 j
                let t6 = Field.sub t6 t

                let x3 = t6

                let t  = Field.sub v x3
                let t4 = Field.mul s1 j
                let t6 = Field.add t4 t4
                let t4 = Field.mul r t
                let y3 = Field.sub t4 t6

                let t  = Field.add a.Z b.Z
                let t4 = Field.mul t t
                let t  = Field.sub t4 z12
                let t4 = Field.sub t z22
                let z3 = Field.mul t4 h

                { X = x3; Y = y3; Z = z3; T = Field.mul z3 z3 }

    // Neg (Go's Neg)
    let neg (a: CurvePoint) : CurvePoint =
        if isInfinity a then clone a
        else
            { X = a.X
              Y = Field.sub 0I a.Y
              Z = a.Z
              T = 0I }   // Go sets t = 0 on Neg

    // Simple scalar multiplication using Double/Add
    let scalarMult (p: CurvePoint) (k: BigInteger) : CurvePoint =
        if k.Sign <= 0 then clone zero
        else
            let mutable res = clone zero
            let mutable baseP = clone p
            let mutable n = k

            while n > 0I do
                if not n.IsEven then
                    res <- add res baseP
                baseP <- double baseP
                n <- n >>> 1

            res

    // Affine printer compatible with Go
    let toString (p: CurvePoint) =
        let q = clone p
        makeAffine q
        sprintf "(%s,%s)" (q.X.ToString()) (q.Y.ToString())

    let pointToString (p: CurvePoint) =
        let q = clone p
        makeAffine q
        let xHex = q.X.ToString("x").PadLeft(64, '0')
        let yHex = q.Y.ToString("x").PadLeft(64, '0')
        sprintf "bn256.G1(%s, %s)" xHex yHex



