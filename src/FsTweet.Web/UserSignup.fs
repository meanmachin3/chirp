namespace UserSignup

module Domain =
  open Chessie
  open Chessie.ErrorHandling
  open User

  type UserSignupRequest = {
    Username: Username
    Password: Password
    EmailAddress: EmailAddress
  } with
    static member TryCreate (username, password, emailAddress) =
      trial {
        let! username = Username.TryCreate username
        let! password = Password.TryCreate password
        let! emailAddress = EmailAddress.TryCreate emailAddress
        return {
          Username = username
          Password = password
          EmailAddress = emailAddress
        }
      }

  type CreateUserRequest = {
    Username: Username
    PasswordHash: PasswordHash
    EmailAddress: EmailAddress
    VerificationCode: VerificationCode
  }

  type SendSignupEmailRequest = {
    Username: Username
    EmailAddress: EmailAddress
    VerificationCode: VerificationCode
  }

  type CreateUserError =
  | EmailAlreadyExists
  | UsernameAlreadyExists
  | Error of System.Exception

  type CreateUser = CreateUserRequest -> AsyncResult<UserId, CreateUserError>

  type SendEmailError = SendEmailError of System.Exception

  type SendSignupEmail = SendSignupEmailRequest -> AsyncResult<unit, SendEmailError>

  type UserSignupError =
  | CreateUserError of CreateUserError
  | SendSignupEmailError of SendEmailError

  type UserSignup =
    CreateUser ->
      SendSignupEmail ->
      UserSignupRequest ->
      AsyncResult<UserId, UserSignupError>

  let userSignup
    (createUser: CreateUser)
    (sendSignupEmail: SendSignupEmail)
    (userSignupRequest: UserSignupRequest) = asyncTrial {
      let verificationCode = VerificationCode.Create()
      let createUserRequest = {
        Username = userSignupRequest.Username
        PasswordHash = PasswordHash.Create userSignupRequest.Password
        EmailAddress = userSignupRequest.EmailAddress
        VerificationCode = verificationCode
      }
      let! userId = createUser createUserRequest |> AR.mapFailure CreateUserError
      let sendSignupEmailRequest = {
        Username = userSignupRequest.Username
        EmailAddress = userSignupRequest.EmailAddress
        VerificationCode = verificationCode
      }
      do! sendSignupEmail sendSignupEmailRequest |> AR.mapFailure SendSignupEmailError
      return userId
    }

  type VerifyUser = string -> AsyncResult<Username option, System.Exception>  

module Persistence =
  open Chessie
  open Chessie.ErrorHandling
  open Database
  open Domain
  open Microsoft.EntityFrameworkCore
  open Npgsql
  open User

  let private (|UniqueViolation|_|) constraintName (ex: System.Exception) =
    match ex with
    | :? System.AggregateException as agEx ->
      // EF Core seems to wrap the PostgresException inside a DbUpdateException
      let ie = agEx.Flatten().InnerException
      let iie = ie.InnerException
      let innerException = if iie <> null then iie else ie
      match innerException with
      | :? PostgresException as pgEx ->
        match pgEx.ConstraintName = constraintName && pgEx.SqlState = "23505" with
        | true -> Some()
        | _ -> None
      | _ -> None
    | _ -> None

  let private mapException = function
    | UniqueViolation "IX_Users_Username" _ -> UsernameAlreadyExists
    | UniqueViolation "IX_Users_Email" _ -> EmailAlreadyExists
    | ex -> Error ex
  
  let createUser (getDataContext: GetDataContext) (createUserRequest: CreateUserRequest) = asyncTrial {
    use dbContext = getDataContext ()
    let newUser: Database.User = {
      Id = 0
      Username = createUserRequest.Username.Value
      PasswordHash = createUserRequest.PasswordHash.Value
      Email = createUserRequest.EmailAddress.Value
      EmailVerificationCode = createUserRequest.VerificationCode.Value
      IsEmailVerified = true
    }
    dbContext.Users.Add(newUser) |> ignore
    do! saveChangesAsync dbContext |> AR.mapFailure mapException
    printfn "[createUser] user created: %A" newUser
    return newUser.Id |> UserId
  }

  let toOption = function
    | Pass x -> Some x
    | _ -> None

  let verifyUser (getDataContext: GetDataContext) (verificationCode: string) = asyncTrial {
    use dbContext = getDataContext ()
    let queryable =
      query {
        for user in dbContext.Users do
          where (user.EmailVerificationCode = verificationCode)
          select user
      }
    let! userToVerify =
      EntityFrameworkQueryableExtensions.ToListAsync(queryable)
      |> Async.AwaitTask
      |> AR.catch
      |> AR.mapSuccess (List.ofSeq >> List.tryHead)
    match userToVerify with
    | None -> return None
    | Some user ->
      let user' = { user with EmailVerificationCode = ""; IsEmailVerified = true }
      dbContext.Entry(user).CurrentValues.SetValues(user')
      do! saveChangesAsync dbContext
      let! username = Username.TryCreateAsync user.Username
      return Some username
  }

module Email =
  open Chessie
  open Chessie.ErrorHandling
  open Domain
  open Email

  let sendSignupEmail sendEmail sendSignupEmailRequest = asyncTrial {
    let placeHolders =
      Map.empty
        .Add("username", sendSignupEmailRequest.Username.Value)
        .Add("verificationCode", sendSignupEmailRequest.VerificationCode.Value)
    let templatedEmail = {
      To = sendSignupEmailRequest.EmailAddress.Value
      TemplateId = int64(7909690)
      PlaceHolders = placeHolders
    }      
    do! sendEmail templatedEmail |> AR.mapFailure SendEmailError
    printfn "[sendSignupEmail] email sent: %A" sendSignupEmailRequest
  }

module Suave =
  open Chessie
  open Chessie.ErrorHandling
  open Domain
  open Suave
  open Suave.DotLiquid
  open Suave.Filters
  open Suave.Form
  open Suave.Operators
  open User

  let private signupTemplatePath = "user/signup.liquid"
  let private signupSuccessTemplatePath = "user/signup_success.liquid"
  let private verificationSuccessPath = "user/verification_success.liquid"
  let private notFoundPath = "not_found.liquid"
  let private serverErrorPath = "server_error.liquid"

  type UserSignupViewModel = {
    Username: string
    Email: string
    Password: string
    Error: string option
  }

  let private emptyUserSignupViewModel = {
    Username = ""
    Email = ""
    Password = ""
    Error = None
  }

  let private onUserSignupSuccess viewModel _ =
    sprintf "/signup/success/%s" viewModel.Username |> Redirection.FOUND

  let private handleCreateUserError viewModel = function
  | EmailAlreadyExists ->
    let viewModel = { viewModel with Error = Some "EmailAddress already exists" }
    page signupTemplatePath viewModel
  | UsernameAlreadyExists ->
    let viewModel = { viewModel with Error = Some "Username already exists" }
    page signupTemplatePath viewModel
  | Error ex ->
    printfn "[handleCreateUserError] server error: %A" ex
    let viewModel = { viewModel with Error = Some "Something went wrong" }
    page signupTemplatePath viewModel

  let private handleSendSignupEmailError viewModel err =
    printfn "[handleSendSignupEmailError] error while sending signup email: %A" err
    let msg = "Something went wrong"
    let viewModel = { viewModel with Error = Some msg }
    page signupTemplatePath viewModel

  let private onUserSignupError viewModel err =
    match err with
    | CreateUserError err -> handleCreateUserError viewModel err
    | SendSignupEmailError err -> handleSendSignupEmailError viewModel err

  let private handleUserSignup userSignup ctx = async {
    match bindEmptyForm ctx.request with
    | Choice1Of2 (viewModel: UserSignupViewModel) ->
      let result =
        UserSignupRequest.TryCreate(
          viewModel.Username,
          viewModel.Password,
          viewModel.Email)
      match result with
      | Success userSignupRequest ->
        let! webpart =
          userSignup userSignupRequest
          |> AR.either (onUserSignupSuccess viewModel) (onUserSignupError viewModel)
        return! webpart ctx
      | Failure error ->
        let viewModel' = { viewModel with Error = Some error }
        return! page signupTemplatePath viewModel' ctx
    | Choice2Of2 error ->
      let viewModel' = { emptyUserSignupViewModel with Error = Some error }
      return! page signupTemplatePath viewModel' ctx
  }

  let private onVerificationSuccess verificationCode username =
    match username with
    | Some (username: Username) ->
      page verificationSuccessPath username.Value
    | _ ->
      printfn "[onVerificationSuccess] invalid verification code: %s" verificationCode
      page notFoundPath "Invalid verification code"

  let private onVerificationFailure verificationCode (ex: System.Exception) =
    printfn
      "[onVerificationFailure] error while verifying email for verification code %s: %A"
      verificationCode
      ex
    page serverErrorPath "Error while verifying email"

  let private handleSignupVerify (verifyUser: VerifyUser) verificationCode ctx = async {
    let! webpart =
      verifyUser verificationCode
      |> AR.either
        (onVerificationSuccess verificationCode)
        (onVerificationFailure verificationCode)
    return! webpart ctx
  }

  let webPart getDataContext sendEmail =
    let createUser = Persistence.createUser getDataContext
    let sendSignupEmail = Email.sendSignupEmail sendEmail
    let userSignup = Domain.userSignup createUser sendSignupEmail
    let verifyUser = Persistence.verifyUser getDataContext
    choose [
      path "/signup" >=>
        choose [
          GET >=> page signupTemplatePath emptyUserSignupViewModel
          POST >=> handleUserSignup userSignup
        ]
      pathScan "/signup/success/%s" (page signupSuccessTemplatePath)      
      pathScan "/signup/verify/%s" (handleSignupVerify verifyUser)
    ]
