namespace Auth

open UserSignup
open Suave
module Domain =
  open Chessie
  open Chessie.ErrorHandling
  open User

  type LoginRequest = {
    Username: Username
    Password: Password
  } with
    static member TryCreate (username, password) =
      trial {
        let! username = Username.TryCreate username
        let! password = Password.TryCreate password
        return {
          Username = username
          Password = password
        }
      }

  type LoginError =
  | InvalidUsernameOrPassword
  | EmailNotVerified
  | Error of System.Exception    

  type Login = FindUser -> LoginRequest -> AsyncResult<User, LoginError>    

  let login (findUser: FindUser) (loginRequest: LoginRequest) = asyncTrial {
    let! maybeUser = findUser loginRequest.Username |> AR.mapFailure Error
    match maybeUser with
    | None -> return! InvalidUsernameOrPassword |> AR.fail
    | Some user ->
      match user.UserEmailAddress with
      | NotVerified _ -> return! EmailNotVerified |> AR.fail
      | Verified _ ->
        match PasswordHash.VerifyPassword loginRequest.Password user.PasswordHash with
        | false -> return! InvalidUsernameOrPassword |> AR.fail
        | true -> return user
  } 

module Suave =
  open Chessie
  open Chessie.ErrorHandling
  open Domain
  open Suave
  open Suave.Authentication
  open Suave.Cookie
  open Suave.DotLiquid
  open Suave.Filters
  open Suave.Form
  open Suave.Operators
  open Suave.State.CookieStateStore
  open User

  let private loginTemplatePath = "user/login.liquid"

  type LoginViewModel = {
    Username: string
    Password: string
    Error: string option
  }

  let private emptyLoginViewModel = {
    Username = ""
    Password = ""
    Error = None
  }

  let private setState key value ctx =
    match HttpContext.state ctx with
    | Some state -> state.set key value
    | None -> never

  let private userSessionKey = "FsTweetUser"

  let private createUserSession (user: User) =
    statefulForSession >=> context (setState userSessionKey user)

  let private renderLoginPage (viewModel: LoginViewModel) maybeUser =
    match maybeUser with
    | Some _ -> Redirection.FOUND "/wall"
    | None -> page loginTemplatePath viewModel

  let private redirectToLoginPage =
    Redirection.FOUND "/login"

  let private retrieveUser ctx: User option =
    match HttpContext.state ctx with
    | Some state -> state.get userSessionKey
    | None -> None

  let private initUserSession fFailure fSuccess ctx: WebPart =
    match retrieveUser ctx with
    | Some user -> fSuccess user
    | None -> fFailure

  let private userSession fFailure fSuccess: WebPart =
    statefulForSession >=> context (initUserSession fFailure fSuccess)

  let private onAuthenticate fSuccess fFailure =
    authenticate CookieLife.Session false
      (fun _ -> Choice2Of2 fFailure)
      (fun _ -> Choice2Of2 fFailure)
      (userSession fFailure fSuccess)

  let requiresAuth fSuccess =
    onAuthenticate fSuccess redirectToLoginPage

  let requiresAuth2 fSuccess =
    onAuthenticate fSuccess JSON.unauthorized

  let private optionalUserSession (fSuccess: User option -> WebPart): WebPart =
    statefulForSession >=> context (retrieveUser >> fSuccess)

  let maybeRequiresAuth fSuccess =
    authenticate CookieLife.Session false
      (fun _ -> Choice2Of2 (fSuccess None))
      (fun _ -> Choice2Of2 (fSuccess None))
      (optionalUserSession fSuccess)

  let private onLoginFailure viewModel loginError =
    let error =
      match loginError with
      | InvalidUsernameOrPassword ->
        "Invalid username or password"
      | EmailNotVerified ->
        "Email not verified"
      | Error ex ->
        printfn "[onLoginFailure] %A" ex
        "Something went wrong"
    let viewModel' = { viewModel with LoginViewModel.Error = Some error }
    renderLoginPage viewModel' None

  let private onLoginSuccess (user: User): WebPart =
    authenticated CookieLife.Session false
      >=> createUserSession user
      >=> Redirection.FOUND "/wall"

  let private handleUserLogin findUser ctx = async {
    match bindEmptyForm ctx.request with
    | Choice1Of2 (viewModel: LoginViewModel) ->
      let result = LoginRequest.TryCreate (viewModel.Username, viewModel.Password)
      match result with
      | Success loginRequest ->
        let! webpart =
          login findUser loginRequest
          |> AR.either onLoginSuccess (onLoginFailure viewModel)
        return! webpart ctx
      | Failure error ->
        let viewModel' = { viewModel with Error = Some error }
        return! renderLoginPage viewModel' None ctx 
    | Choice2Of2 error ->
      let viewModel' = { emptyLoginViewModel with Error = Some error }
      return! renderLoginPage viewModel' None ctx
  }  

  let webpart getDataContext =
    let findUser = Persistence.findUser getDataContext
    choose [
      path "/login" >=>
        choose [
          GET >=> maybeRequiresAuth (renderLoginPage emptyLoginViewModel)
          POST >=> handleUserLogin findUser
        ]
      path "/logout" >=> GET >=> deauthenticate >=> redirectToLoginPage
    ]
