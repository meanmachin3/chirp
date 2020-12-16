$(() => {
  $('#follow').on('click', () => {
    const $this = $(event.currentTarget)
    const userId = $this.data('user-id')
    $this.prop('disabled', true)
    $.ajax({
      url : '/follow',
      type: 'post',
      data: JSON.stringify({userId}),
      contentType: 'application/json'
    }).done(() => {
      alert('Successfully followed')
      $this.prop('disabled', false)
    }).fail((jqXHR, textStatus, errorThrown) => {
      console.log({jqXHR, textStatus, errorThrown})
      alert('Something went wrong!')
    })
  })

  const usersTemplate =
    `{{#users}}
    
      <div class="well user-card">
        <a href="/{{username}}">@{{username}}</a>
      </div>
    {{/users}}
    `

  const renderUsers = (data, $body, $count) => {
    const htmlOutput = Mustache.render(usersTemplate, data)
    $body.html(htmlOutput)
    $count.html(data.users.length)
  }

  const loadFollowers = () => {
    const url = `/${fsTweet.user.id}/followers`
    $.getJSON(url, data => renderUsers(data, $('#followers'), $('#followersCount')))
  }

  const loadFollowees = () => {
    const url = `/${fsTweet.user.id}/followees`
    $.getJSON(url, data => renderUsers(data, $('#following'), $('#followingCount')))
  }

  const tweetTemplate =
  `{{#users}}
    <div class="tweet-1">
          <div class="tweet-txt">
            <div class="tweet-name-date">
            <a href="/{{username}}"><span class="twitter-account"> @{{username}}</span></a>
            </div>
            <div class="message">
              {{post}}
            </div>
            <div class="tweet-icons">
              <i class="fas fa-image"></i>
              <i class="fas fa-gift"></i>
              <i class="fas fa-retweet"></i>
              <i class="fas fa-heart"></i>
            </div>
          </div>
        </div>
  {{/users}}
  `
  const renderTweets = (data, $body) => {
    const htmlOutput = Mustache.render(tweetTemplate, data)
    $body.html(htmlOutput)
  }

  const loadAllTweets = () => {
    const url = `/all`
    $.getJSON(url, data => renderTweets(data, $('#result')))
  }

  function filter() {
    // Declare variables
    var input, filter, ul, li, a, i, txtValue;
    input = document.getElementById('search');
    filter = input.value.replace("#",'').replace("@",'').toUpperCase();
    ul = document.getElementsByClassName("message");
    debugger;
    // Loop through all list items, and hide those who don't match the search query
    for (i = 0; i < ul.length; i++) {
      a = ul[i]
      txtValue = a.textContent || a.innerText;
      if (txtValue.toUpperCase().indexOf(filter) > -1) {
        ul[i].parentElement.parentElement.style.display = "";
      } else {
        ul[i].parentElement.parentElement.style.display = "none";
      }
    }
  }

  $('#search').on('keyup', filter)

  loadFollowers()
  loadFollowees()
  loadAllTweets()
})
