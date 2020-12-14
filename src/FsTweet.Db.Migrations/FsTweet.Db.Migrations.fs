namespace FsTweet.Db.Migrations

open FluentMigrator
open System.Xml

[<Migration(201808011935L, "Creating Users Table")>]
type CreateUserTable() =
  inherit Migration()
  override this.Up() =
    base.Create.Table("Users")
      .WithColumn("Id").AsInt32().PrimaryKey().Identity()
      .WithColumn("Username").AsString(12).Unique().NotNullable()
      .WithColumn("Email").AsString(254).Unique().NotNullable()
      .WithColumn("PasswordHash").AsString().NotNullable()
      .WithColumn("EmailVerificationCode").AsString().NotNullable()
      .WithColumn("IsEmailVerified").AsBoolean()
    |> ignore
  override this.Down() =
    base.Delete.Table("Users") |> ignore

[<Migration(201808180721L, "Creating Tweets Table")>]
type CreateTweetTable() =
  inherit Migration()
  override this.Up() =
    base.Create.Table("Tweets")
      .WithColumn("Id").AsGuid().PrimaryKey()
      .WithColumn("Post").AsString(144).NotNullable()
      .WithColumn("UserId").AsInt32().ForeignKey("Users", "Id")
      .WithColumn("TweetedAt").AsDateTimeOffset().NotNullable()
    |> ignore
  override this.Down() =
    base.Delete.Table("Tweets") |> ignore

[<Migration(201808212149L, "Creating Social Table")>]
type CreateSocialTable() =
  inherit Migration()
  override this.Up() =
    base.Create.Table("Social")
      .WithColumn("Id").AsGuid().PrimaryKey()
      .WithColumn("FollowerUserId").AsInt32().ForeignKey("Users", "Id").NotNullable()
      .WithColumn("FollowingUserId").AsInt32().ForeignKey("Users", "Id").NotNullable()
    |> ignore

    base.Create.UniqueConstraint("SocialRelationship")
      .OnTable("Social")
      .Columns("FollowerUserId", "FollowingUserId")
    |> ignore

  override this.Down() =
    base.Delete.Table("Social") |> ignore
