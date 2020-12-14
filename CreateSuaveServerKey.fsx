#r "nuget: Suave"

open Suave.Utils
open System

Crypto.generateKey Crypto.KeyLength
|> Convert.ToBase64String
|> printfn "%s"
