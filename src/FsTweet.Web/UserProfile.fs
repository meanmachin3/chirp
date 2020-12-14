namespace UserProfile

module Domain =
  open Chessie.ErrorHandling
  open Social.Domain
  open System.Security.Cryptography
  open User

  type UserProfileType =
  | Self
  | OtherNotFollowing
  | OtherFollowing

  type UserProfile = {
    User: User
    GravatarUrl: string
    UserProfileType: UserProfileType
  }

  let private gravatarUrl (userEmailAddress: UserEmailAddress) =
    use md5 = MD5.Create()
    userEmailAddress.Value
    |> System.Text.Encoding.Default.GetBytes
    |> md5.ComputeHash
    |> Array.map (fun b -> b.ToString("x2"))
    |> String.concat ""
    |> sprintf "http://www.gravatar.com/avatar/%s?s=200"

  let private newProfile userProfileType user = {
    User = user
    GravatarUrl = gravatarUrl user.UserEmailAddress
    UserProfileType = userProfileType
  }

  type FindUserProfile =
    Username -> User option -> AsyncResult<UserProfile option, System.Exception>

  let findUserProfile
    (findUser: FindUser)
    (isFollowing: IsFollowing)
    (username: Username)
    maybeLoggedInUser = asyncTrial {

    match maybeLoggedInUser with
    | None ->
      let! maybeUser = findUser username
      return Option.map (newProfile OtherNotFollowing) maybeUser
    | Some (user: User) ->
      if user.Username = username then
        let userProfile = newProfile Self user
        return Some userProfile
      else
        let! maybeUser = findUser username
        match maybeUser with
        | Some otherUser ->
          let! isFollowingOtherUser = isFollowing user otherUser.UserId
          let userProfileType =
            if isFollowingOtherUser
              then OtherFollowing
              else OtherNotFollowing
          let userProfile = newProfile userProfileType otherUser
          return Some userProfile                    
        | None -> return None
  }

module Suave =
  open Auth
  open Chessie
  open Database
  open Domain
  open Social
  open Suave.DotLiquid
  open Suave.Filters
  open User

  type UserProfileViewModel = {
    Username: string
    UserId: int
    GravatarUrl: string
    IsSelf: bool
    IsLoggedIn: bool
    IsFollowing: bool
    ApiKey: string
    AppId: string
    UserFeedToken: string
  }

  let private newUserProfileViewModel
    (getStreamClient: GetStream.Client)
    (userProfile: UserProfile) =

    let (UserId userId) = userProfile.User.UserId
    let isSelf, isFollowing =
      match userProfile.UserProfileType with
      | Self -> true, false
      | OtherFollowing -> false, true
      | OtherNotFollowing -> false, false
    let userFeed = GetStream.userFeed getStreamClient userId
    
    {
      Username = userProfile.User.Username.Value
      UserId = userId
      GravatarUrl = userProfile.GravatarUrl
      IsSelf = isSelf
      IsFollowing = isFollowing
      IsLoggedIn = false
      ApiKey = getStreamClient.Config.ApiKey
      AppId = getStreamClient.Config.AppId
      UserFeedToken = userFeed.ReadOnlyToken
    }

  let private renderUserProfilePage (viewModel: UserProfileViewModel) =
    page "user/profile.liquid" viewModel

  let private renderProfileNotFound =
    page "not_found.liquid" "User not found"

  let private onFindUserProfileSuccess newUserProfileViewModel isLoggedIn maybeUserProfile =
    match maybeUserProfile with
    | Some (userProfile: UserProfile) ->
      let viewModel = { newUserProfileViewModel userProfile with IsLoggedIn = isLoggedIn }
      renderUserProfilePage viewModel
    | None -> renderProfileNotFound

  let private onFindUserProfileFailure (ex: System.Exception) =
    printfn "[onFindUserProfileFailure] %A" ex
    page "server_error.liquid" "Something went wrong!"

  let private renderUserProfile
    newUserProfileViewModel
    findUserProfile
    username
    maybeLoggedInUser
    ctx = async {

    match Username.TryCreate username with
    | Success validatedUsername ->
      let isLoggedIn = Option.isSome maybeLoggedInUser
      let! webpart =
        findUserProfile validatedUsername maybeLoggedInUser
        |> AR.either (onFindUserProfileSuccess newUserProfileViewModel isLoggedIn) onFindUserProfileFailure
      return! webpart ctx      
    | Failure _ ->
      return! renderProfileNotFound ctx
  }

  let webpart (getDataContext: GetDataContext) getStreamClient =
    let findUser = Persistence.findUser getDataContext
    let isFollowing = Persistence.isFollowing getDataContext
    let findUserProfile = findUserProfile findUser isFollowing
    let newUserProfileViewModel = newUserProfileViewModel getStreamClient
    let renderUserProfile = renderUserProfile newUserProfileViewModel findUserProfile
    pathScan "/%s" (fun username -> Suave.maybeRequiresAuth (renderUserProfile username))
