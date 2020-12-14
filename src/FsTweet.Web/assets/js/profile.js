$(() => {
  const client = stream.connect(fsTweet.stream.apiKey, null, fsTweet.stream.appId)
  const userFeed = client.feed('user', fsTweet.user.id, fsTweet.user.userFeedToken)

  userFeed.get({ limit: 25 })
    .then(body => {
      $(body.results.reverse()).each((_, tweet) => {
        renderTweet($('#tweets'), tweet)
      })
    })
})
