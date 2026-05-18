namespace TelaCrypto

module Keccak256 =
    open System

    // Internal sponge implementation
    let private keccakInternal (rate: int) (capacity: int) (outputLength: int) (input: byte[]) =
        let rounds = 24
        let w = 64
        let laneCount = (rate + capacity) / w
        let state = Array.zeroCreate<UInt64> laneCount

        let rot (x: UInt64) (n: int) =
            (x <<< n) ||| (x >>> (64 - n))

        let rc =
            [| 0x0000000000000001UL; 0x0000000000008082UL; 0x800000000000808aUL; 0x8000000080008000UL;
               0x000000000000808bUL; 0x0000000080000001UL; 0x8000000080008081UL; 0x8000000000008009UL;
               0x000000000000008aUL; 0x0000000000000088UL; 0x0000000080008009UL; 0x000000008000000aUL;
               0x000000008000808bUL; 0x800000000000008bUL; 0x8000000000008089UL; 0x8000000000008003UL;
               0x8000000000008002UL; 0x8000000000000080UL; 0x000000000000800aUL; 0x800000008000000aUL;
               0x8000000080008081UL; 0x8000000000008080UL; 0x0000000080000001UL; 0x8000000080008008UL |]

        // Pad input
        let rateBytes = rate / 8
        let mutable padded = Array.copy input
        padded <- Array.append padded [| 0x01uy |]
        while padded.Length % rateBytes <> rateBytes - 1 do
            padded <- Array.append padded [| 0x00uy |]
        padded <- Array.append padded [| 0x80uy |]

        // Absorb
        for blockStart in 0 .. rateBytes .. (padded.Length - rateBytes) do
            for i in 0 .. (rateBytes/8 - 1) do
                let mutable v = 0UL
                for b in 0 .. 7 do
                    v <- v ||| (uint64 padded.[blockStart + i*8 + b] <<< (8*b))
                state.[i] <- state.[i] ^^^ v

            // Keccak-f permutation
            for round in 0 .. rounds-1 do
                // θ
                let c = Array.init 5 (fun x -> state.[x] ^^^ state.[x+5] ^^^ state.[x+10] ^^^ state.[x+15] ^^^ state.[x+20])
                let d = Array.init 5 (fun x -> c.[(x+4)%5] ^^^ rot c.[(x+1)%5] 1)
                for x in 0 .. 4 do
                    for y in 0 .. 4 do
                        state.[x + 5*y] <- state.[x + 5*y] ^^^ d.[x]

                // ρ and π
                let mutable x = 1
                let mutable y = 0
                let mutable current = state.[1]
                for i in 0 .. 23 do
                    let newX = y
                    let newY = (2*x + 3*y) % 5
                    let idx = newX + 5*newY
                    let tmp = state.[idx]
                    state.[idx] <- rot current (((i+1)*(i+2)/2) % 64)
                    current <- tmp
                    x <- newX
                    y <- newY

                // χ
                for y in 0 .. 4 do
                    let row = [| for x in 0 .. 4 -> state.[x + 5*y] |]
                    for x in 0 .. 4 do
                        state.[x + 5*y] <- row.[x] ^^^ ((~~~row.[(x+1)%5]) &&& row.[(x+2)%5])

                // ι
                state.[0] <- state.[0] ^^^ rc.[round]

        // Squeeze
        let mutable out = Array.zeroCreate<byte> outputLength
        let mutable outPos = 0
        while outPos < outputLength do
            for i in 0 .. (rateBytes/8 - 1) do
                let v = state.[i]
                for b in 0 .. 7 do
                    if outPos < outputLength then
                        out.[outPos] <- byte ((v >>> (8*b)) &&& 0xFFUL)
                        outPos <- outPos + 1
        out

    // Public API: Keccak-256
    let compute (data: byte[]) =
        keccakInternal 1088 512 32 data
