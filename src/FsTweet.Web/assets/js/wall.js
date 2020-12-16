$(() => {
  $('#tweetForm').submit(event => {
    const $this = $(this)
    const $tweet = $('#tweet')
    event.preventDefault()
    $this.prop('disabled', true)
    $.ajax({
      url: '/tweets',
      type: 'post',
      data: JSON.stringify({ post: $tweet.val(), retweet: false }),
      contentType: 'application/json'
    }).done(() => {
      $this.prop('disabled', false)
      $tweet.val('')
    }).fail((jqXHR, textStatus, errorThrown) => {
      console.log({ jqXHR, textStatus, errorThrown })
      alert('Something went wrong!')
    })
  })

  $(document).on('click', '.retweet', function(event) {
    const $this = $(this)
    const $tweet = $this.siblings('p').text()
    debugger;
    event.preventDefault()
    $this.prop('disabled', true)
    $.ajax({
      url: '/tweets',
      type: 'post',
      data: JSON.stringify({ post: $tweet.val(), retweet: true }),
      contentType: 'application/json'
    }).done(() => {
      $this.prop('disabled', false)
      $tweet.val('')
    }).fail((jqXHR, textStatus, errorThrown) => {
      console.log({ jqXHR, textStatus, errorThrown })
      alert('Something went wrong!')
    })
  })

  // const client = stream.connect(fsTweet.stream.apiKey, null, fsTweet.stream.appId)
  // const userFeed = client.feed('user', fsTweet.user.id, fsTweet.user.userFeedToken)
  // const timelineFeed = client.feed('timeline', fsTweet.user.id, fsTweet.user.timelineToken)

  // userFeed.subscribe(data => {
  //   renderTweet($('#wall'), data.new[0])
  // })

  // timelineFeed.subscribe(data => {
  //   renderTweet($('#wall'), data.new[0])
  // })

  // timelineFeed.get({ limit: 25 })
  //   .then(body => {
  //     const timelineTweets = body.results
  //     userFeed.get({ limit: 25 })
  //       .then(body => {
  //         const userTweets = body.results
  //         const allTweets = $.merge(timelineTweets, userTweets)
  //         allTweets.sort((t1, t2) => new Date(t2.time) - new Date(t1.time))
  //         $(allTweets.reverse()).each((_, tweet) => {
  //           renderTweet($('#wall'), tweet)
  //         })
  //       })
  //   })

  const usersTemplate =
  `{{#users}}
    <div class="well user-card">
      <a href="/{{username}}">@{{username}}</a>
      <p>{{post}}</p>
    </div>
  {{/users}}
  `

  const renderUsers = (data, $body, $count) => {
    debugger;
    const htmlOutput = Mustache.render(usersTemplate, data)
    $body.html(htmlOutput)
  }

  const loadTweets = () => {
    const url = `/timeline`
    $.getJSON(url, data => renderUsers(data, $('#wall')))
  }

  loadTweets()
})
