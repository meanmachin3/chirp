module User

open BCrypt.Net
open Chessie
open Chessie.ErrorHandling
open System.Security.Cryptography
open System.Runtime.InteropServices.ComTypes

let private base64URLEncoding bytes =
  let base64String = System.Convert.ToBase64String bytes
  base64String
    .TrimEnd([|'='|])
    .Replace('+', '-')
    .Replace('/', '_')

type UserId = UserId of int

type Username = private Username of string with
  static member TryCreate (username: string) =
    match username with
    | null | "" -> fail "Username should not be empty"
    | x when x.Length > 12 -> fail "Username should not be more than 12 characters"
    | x -> x.Trim().ToLowerInvariant() |> Username |> ok
  static member TryCreateAsync username =
    Username.TryCreate username
    |> mapFirstFailure System.Exception
    |> Async.singleton
    |> AR
  member this.Value =
    let (Username username) = this
    username

type Password = private Password of string with
  static member TryCreate (password: string) =
    match password with
    | null | "" -> fail "Password should not be empty"
    | x when x.Length < 4 || x.Length > 8 -> fail "Password should contain only 4-8 characters"
    | x -> Password x |> ok
  member this.Value =
    let (Password password) = this
    password

type PasswordHash = private PasswordHash of string with
  static member Create (password: Password) =
    BCrypt.HashPassword(password.Value) |> PasswordHash
  static member TryCreate (passwordHash: string) =
    try
      BCrypt.InterrogateHash passwordHash |> ignore
      PasswordHash passwordHash |> ok
    with
    | _ -> fail "Invalid password hash"
  static member VerifyPassword (password: Password) (passwordHash: PasswordHash) =
    BCrypt.Verify(password.Value, passwordHash.Value)  
  member this.Value =
    let (PasswordHash passwordHash) = this
    passwordHash

type VerificationCode = private VerificationCode of string with
  static member Create () =
    let verificationCodeLength = 15
    let bytes:  byte [] = Array.zeroCreate verificationCodeLength
    use rng = new RNGCryptoServiceProvider()
    rng.GetBytes(bytes)
    base64URLEncoding(bytes) |> VerificationCode
  member this.Value =
    let (VerificationCode verificationCode) = this
    verificationCode

type EmailAddress = private EmailAddress of string with
  static member TryCreate (emailAddress: string) =
    try
      new System.Net.Mail.MailAddress(emailAddress) |> ignore
      emailAddress.Trim().ToLowerInvariant() |> EmailAddress |> ok
    with
      | _ -> fail "Invalid email adddress"
  member this.Value =
    let (EmailAddress emailAddress) = this
    emailAddress

type UserEmailAddress =
| Verified of EmailAddress
| NotVerified of EmailAddress
with
  member this.Value =
    match this with
    | Verified e | NotVerified e -> e.Value

type User = {
  UserId: UserId
  Username: Username
  UserEmailAddress: UserEmailAddress
  PasswordHash: PasswordHash
}

type FindUser = Username -> AsyncResult<User option, System.Exception>

module Persistence =
  open Database
  open Microsoft.EntityFrameworkCore
  open System

  let mapUserEntityToUser (userEntity: Database.User) =
    let userResult = trial {
      let! username = Username.TryCreate userEntity.Username
      let! passwordHash = PasswordHash.TryCreate userEntity.PasswordHash
      let! email = EmailAddress.TryCreate userEntity.Email
      let userEmailAddress =
        match userEntity.IsEmailVerified with
        | true -> Verified email
        | false -> NotVerified email
      return {
        UserId = UserId userEntity.Id
        Username = username
        PasswordHash = passwordHash
        UserEmailAddress = userEmailAddress
      }      
    }
    userResult |> mapFirstFailure Exception

  let private mapUserEntity (userEntity: Database.User) =
    mapUserEntityToUser userEntity
    |> Async.singleton
    |> AR

  let mapUserEntities (userEntities: Database.User seq) =
    userEntities
    |> Seq.map mapUserEntityToUser
    |> collect
    |> mapFirstFailure (fun ex -> new AggregateException(ex) :> Exception)
    |> Async.singleton
    |> AR

  let findUser (getDataContext: GetDataContext) (username: Username) = asyncTrial {
    use dbContext = getDataContext ()
    let queryable =
      query {
        for user in dbContext.Users do
          where (user.Username = username.Value)
          select user
      }
    let! userToFind =
      EntityFrameworkQueryableExtensions.ToListAsync(queryable)
      |> Async.AwaitTask
      |> AR.catch
      |> AR.mapSuccess (List.ofSeq >> List.tryHead)
    match userToFind with
    | None -> return None
    | Some userEntity ->
      let! user = mapUserEntity userEntity
      return Some user
  }
